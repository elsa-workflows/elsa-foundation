using Kernel.Durable;
using Kernel.Runtime;
using Npgsql;
using NpgsqlTypes;

internal sealed class PgKeyAdmission(NpgsqlDataSource db, int partitions) : IKeyAdmission
{
    private int _requests;
    public int Requests => Volatile.Read(ref _requests);
    public async ValueTask<KeyDecision> AcceptAsync(KeyRequest request, CancellationToken cancellationToken = default)
    {
        request.Validate();
        var partition = InstanceRef.Spread(request.Key, partitions);
        await using var cmd = db.CreateCommand("SELECT * FROM keyed_accept($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)");
        void Param(NpgsqlDbType type, object? value) => cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value ?? DBNull.Value });
        Param(NpgsqlDbType.Text, request.EncodedKey); Param(NpgsqlDbType.Text, request.EncodedOperation); Param(NpgsqlDbType.Text, request.Digest);
        Param(NpgsqlDbType.Text, request.WireId); Param(NpgsqlDbType.Text, request.Kind); Param(NpgsqlDbType.Text, request.Payload);
        Param(NpgsqlDbType.Text, request.Start is null ? null : Json.Write(request.Start)); Param(NpgsqlDbType.Bigint, request.KeepForMillis);
        Param(NpgsqlDbType.Bigint, request.Due ?? 0); Param(NpgsqlDbType.Integer, partition);
        Interlocked.Increment(ref _requests);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Key admission returned no decision.");
        return new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    public async Task PrepareAsync()
    {
        await using var cmd = db.CreateCommand("""
            CREATE SEQUENCE key_physical_run AS bigint NO CYCLE;
            CREATE TABLE key_binding (key_hash text COLLATE "C" NOT NULL,key text COLLATE "C" NOT NULL,physical text NOT NULL,keep_until bigint NOT NULL);
            CREATE INDEX key_binding_bucket ON key_binding(key_hash);
            CREATE TABLE key_receipt (key_hash text COLLATE "C" NOT NULL,operation_hash text COLLATE "C" NOT NULL,
                key text COLLATE "C" NOT NULL,operation text COLLATE "C" NOT NULL,digest text NOT NULL,physical text NOT NULL,keep_until bigint NOT NULL);
            CREATE INDEX key_receipt_bucket ON key_receipt(key_hash,operation_hash);
            CREATE FUNCTION keyed_accept(k text, op text, d text, wire text, kind text, payload text, spec text,
                                         duration bigint, due bigint, part integer)
            RETURNS TABLE(outcome text, physical text, keep_until bigint) LANGUAGE plpgsql VOLATILE AS $$
            DECLARE saved key_receipt; binding key_binding; assigned boolean; retired boolean; ms bigint;
            BEGIN
                -- Hold through commit, including when the key has no binding row yet. Hash collisions serialize only.
                PERFORM pg_advisory_xact_lock(hashtextextended(k COLLATE "C", 4229));
                SELECT * INTO saved FROM key_receipt r WHERE r.key_hash=md5(k) AND r.operation_hash=md5(op) AND r.key=k AND r.operation=op;
                IF FOUND THEN
                    IF saved.digest = d THEN RETURN QUERY SELECT 'accepted'::text, saved.physical, saved.keep_until;
                    ELSE RETURN QUERY SELECT 'conflict'::text, NULL::text, NULL::bigint; END IF;
                    RETURN;
                END IF;
                SELECT * INTO binding FROM key_binding b WHERE b.key_hash=md5(k) AND b.key=k;
                assigned := FOUND;
                SELECT EXISTS(SELECT 1 FROM "Instances" i WHERE i."Id" = binding.physical AND i."Retired") INTO retired;
                ms := floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint;
                IF spec IS NOT NULL THEN
                    IF assigned AND (ms < binding.keep_until OR NOT retired) THEN
                        RETURN QUERY SELECT 'in-use'::text, NULL::text, NULL::bigint; RETURN;
                    END IF;
                    binding.physical := 'g' || part || '/key/' || nextval('key_physical_run'); binding.keep_until := ms + duration;
                    IF assigned THEN
                        UPDATE key_binding b SET physical=binding.physical,keep_until=binding.keep_until WHERE b.key_hash=md5(k) AND b.key=k;
                    ELSE INSERT INTO key_binding VALUES(md5(k),k,binding.physical,binding.keep_until); END IF;
                ELSIF NOT assigned OR retired THEN
                    RETURN QUERY SELECT 'missing'::text, NULL::text, NULL::bigint; RETURN;
                END IF;
                INSERT INTO "Messages" ("InboxScope","Id","Partition","Target","Kind","Payload","Start","Due","Seen","Attempts","Trial","Done")
                VALUES (CASE WHEN spec IS NULL THEN 0 ELSE 9223372036854775807 END,wire,part,binding.physical,kind,payload,spec,due,0,0,0,false);
                INSERT INTO key_receipt VALUES(md5(k),md5(op),k,op,d,binding.physical,binding.keep_until);
                RETURN QUERY SELECT 'accepted'::text, binding.physical, binding.keep_until;
            END $$;
            """);
        await cmd.ExecuteNonQueryAsync();
    }
}
