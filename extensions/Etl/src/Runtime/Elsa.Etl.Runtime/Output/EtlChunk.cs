namespace Elsa.Etl.Runtime.Output;

/// <summary>A complete physical chunk; acceptance in the ETL store is a separate operation.</summary>
internal sealed record EtlChunk(string Key, string ContentHash, long ByteLength);
