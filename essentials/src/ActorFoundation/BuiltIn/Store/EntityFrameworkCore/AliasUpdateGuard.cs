using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.EntityFramework;

// A raw turn may use a provider-equal alias while its row retains the creation spelling.
// Updates add a second Id predicate. Inserts use a same-command, post-insert Id check:
// SQL Server reads EXISTS; MySQL evaluates DO IF(EXISTS(...)) without a result set or write.
// Both use actual column equality. Canonical SQL is untouched. Unknown templates roll back
// before commit through EfStore's GuardedAliases check; no extra driver request is introduced.
internal sealed class AliasUpdateGuard:DbCommandInterceptor
{
    internal static readonly AliasUpdateGuard Instance=new();
    private static readonly Regex Update=new("UPDATE\\s+(?:\\[Instances\\]|\"Instances\"|`Instances`)\\s+SET\\b[\\s\\S]*?\\bWHERE\\s+(?<id>\\[Id\\]|\"Id\"|`Id`)\\s*=\\s*(?<parameter>@[A-Za-z0-9_]+)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex Insert=new("INSERT\\s+INTO\\s+(?<table>\\[Instances\\]|`Instances`)\\s*\\((?<columns>[^)]+)\\)\\s*VALUES\\s*(?<rows>[^;]+);",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    internal static bool Refused(Exception error)
    {
        for(Exception? e=error;e is not null;e=e.InnerException)
            if(e is AliasRefusal)return true;
        return false;
    }
    private static void Apply(DbCommand command,CommandEventData data)
    {
        if(data.CommandSource!=CommandSource.SaveChanges || data.Context is not KernelDb {AliasGuards.Count:>0} db)return;
        command.CommandText=Update.Replace(command.CommandText,match=>
        {
            var parameter=command.Parameters.Cast<DbParameter>().SingleOrDefault(p=>p.ParameterName==match.Groups["parameter"].Value);
            if(parameter?.Value is not string row || !db.AliasGuards.TryGetValue(row,out var actor))return match.Value;
            var guard=command.CreateParameter();guard.ParameterName="@kernel_actor_"+command.Parameters.Count;guard.Value=actor;
            command.Parameters.Add(guard);db.GuardedAliases.Add(row);
            return match.Value+" AND "+match.Groups["id"].Value+" = "+guard.ParameterName;
        });
        if(db.InsertAliasGuards.Count==0)return;
        command.CommandText=Insert.Replace(command.CommandText,match=>
        {
            var columns=match.Groups["columns"].Value.Split(',').Select(c=>c.Trim().Trim('[',']','`')).ToArray();
            var idColumn=Array.IndexOf(columns,"Id");if(idColumn<0)return match.Value;
            var tail=new System.Text.StringBuilder();var sqlServer=match.Groups["table"].Value[0]=='[';
            foreach(Match values in Regex.Matches(match.Groups["rows"].Value,"\\(([^()]+)\\)"))
            {
                var items=values.Groups[1].Value.Split(',');if(items.Length!=columns.Length)throw new StoreException(Failure.Definite,"Unsupported aliased INSERT shape.");
                var key=items[idColumn].Trim();var p=command.Parameters.Cast<DbParameter>().SingleOrDefault(p=>p.ParameterName==key);
                if(p?.Value is not string row || !db.InsertAliasGuards.Contains(row))continue;
                var guard=command.CreateParameter();guard.ParameterName="@kernel_actor_"+command.Parameters.Count;guard.Value=db.AliasGuards[row];command.Parameters.Add(guard);
                if(sqlServer)tail.Append($"\nIF NOT EXISTS(SELECT 1 FROM [Instances] WHERE [Id]={key} AND [Id]={guard.ParameterName}) THROW 51029, 'kernel actor alias mismatch', 1;\n");
                // Cardinality failure is independent of strict mode and affected-row conventions.
                else tail.Append($"\nDO IF(EXISTS(SELECT 1 FROM `Instances` WHERE `Id`={key} AND `Id`={guard.ParameterName}),0,(SELECT n FROM (SELECT 0 AS n UNION ALL SELECT 0) kernel_alias_refusal));\n");
                db.GuardedAliases.Add(row);
            }
            return match.Value+tail;
        });
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c,CommandEventData d,InterceptionResult<DbDataReader> r){Apply(c,d);return r;}
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c,CommandEventData d,InterceptionResult<DbDataReader> r,CancellationToken ct=default){Apply(c,d);return ValueTask.FromResult(r);}
    public override InterceptionResult<int> NonQueryExecuting(DbCommand c,CommandEventData d,InterceptionResult<int> r){Apply(c,d);return r;}
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c,CommandEventData d,InterceptionResult<int> r,CancellationToken ct=default){Apply(c,d);return ValueTask.FromResult(r);}
    private sealed class AliasRefusal(Exception inner):Exception("Actor alias insert guard refused.",inner);
    private static void Failed(DbCommand command,CommandErrorEventData data)
    {
        if(data.CommandSource!=CommandSource.SaveChanges || data.Context is not KernelDb {InsertAliasGuards.Count:>0})return;
        if(data.Exception is Microsoft.Data.SqlClient.SqlException {Number:51029} sql && sql.Message.Contains("kernel actor alias mismatch",StringComparison.Ordinal) && command.CommandText.Contains("THROW 51029",StringComparison.Ordinal) ||
           data.Exception is MySql.Data.MySqlClient.MySqlException {Number:1242} && command.CommandText.Contains("kernel_alias_refusal",StringComparison.Ordinal))throw new AliasRefusal(data.Exception);
    }
    public override void CommandFailed(DbCommand command,CommandErrorEventData data)=>Failed(command,data);
    public override Task CommandFailedAsync(DbCommand command,CommandErrorEventData data,CancellationToken ct=default){Failed(command,data);return Task.CompletedTask;}
}
