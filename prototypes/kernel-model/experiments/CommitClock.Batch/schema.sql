CREATE TABLE fixture_run (version text PRIMARY KEY, database_oid oid NOT NULL, completed boolean NOT NULL);
CREATE TABLE partition (id integer PRIMARY KEY, epoch bigint NOT NULL, position bigint NOT NULL DEFAULT 0, lease_until bigint NOT NULL DEFAULT 0);
CREATE TABLE actor (partition integer NOT NULL REFERENCES partition, occurrence text PRIMARY KEY, revision integer NOT NULL, state text NOT NULL);
CREATE TABLE effect (occurrence text PRIMARY KEY REFERENCES actor, body_count integer NOT NULL CHECK(body_count=1));
CREATE TABLE obligation (partition integer NOT NULL REFERENCES partition, occurrence text PRIMARY KEY REFERENCES actor, original_revision integer NOT NULL, source_xid xid8 NOT NULL, retention_us bigint NOT NULL CHECK(retention_us>0), phase text NOT NULL CHECK(phase IN ('pending','sealed')), anchor_us bigint, CHECK((phase='pending' AND anchor_us IS NULL) OR (phase='sealed' AND anchor_us IS NOT NULL)));
CREATE INDEX pending_obligation ON obligation(partition,occurrence) WHERE phase='pending';
CREATE FUNCTION immutable_source_and_seal() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF ROW(NEW.partition,NEW.occurrence,NEW.original_revision,NEW.source_xid,NEW.retention_us) IS DISTINCT FROM ROW(OLD.partition,OLD.occurrence,OLD.original_revision,OLD.source_xid,OLD.retention_us) OR (OLD.phase='sealed' AND NEW IS DISTINCT FROM OLD) THEN RAISE EXCEPTION 'immutable source or seal' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER immutable_obligation BEFORE UPDATE ON obligation FOR EACH ROW EXECUTE FUNCTION immutable_source_and_seal();
