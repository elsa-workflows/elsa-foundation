using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Persistence.EntityFramework;
using Elsa.Samples.Nuplane.Notes;

namespace Elsa.Samples.Nuplane.Notes.Tooling;

// One design-time factory per provider-derived context. Add a line when the module gains a provider.

public sealed class NotesSqliteDbContextFactory() : ModuleDesignTimeFactory<NotesSqliteDbContext>(EfMigrationsHistory.TableName(NotesModule.HistoryModuleName));

public sealed class NotesPostgreSqlDbContextFactory() : ModuleDesignTimeFactory<NotesPostgreSqlDbContext>(EfMigrationsHistory.TableName(NotesModule.HistoryModuleName));
