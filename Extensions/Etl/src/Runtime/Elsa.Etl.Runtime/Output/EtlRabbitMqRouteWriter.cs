using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Runtime.Output;

/// <summary>One RabbitMQ route delivery: its target and retained body.</summary>
internal sealed record EtlRabbitMqRouteDelivery(
    string ConnectionString, string Exchange, string RoutingKey, ReadOnlyMemory<byte> Body, string EffectId, string RunId,
    string BatchId)
{
    // The connection string holds credentials; never print it.
    public override string ToString() => nameof(EtlRabbitMqRouteDelivery);
}

internal enum EtlRabbitMqWriteOutcome { Confirmed, Yield }

/// <summary>
/// Publishes one RabbitMQ route body as one message per record, in order, and reports it confirmed only once the broker has
/// confirmed every message (spec 1200).
/// </summary>
/// <remarks>
/// A message's id is the effect identity and the record's position, and its headers name the run, batch, effect and
/// position, so a replayed batch republishes byte-identical messages a consumer can recognise. Driver exceptions are wrapped
/// here: a transient failure or a negative acknowledgement yields, and anything deterministic fails the run with a stable code.
/// </remarks>
internal sealed class EtlRabbitMqRouteWriter(IEtlRabbitMqPublisher publisher)
{
    public const string RunIdHeader = "elsa-etl-run-id";
    public const string BatchIdHeader = "elsa-etl-batch-id";
    public const string EffectIdHeader = "elsa-etl-effect-id";
    public const string RecordIndexHeader = "elsa-etl-record-index";

    public async ValueTask<EtlRabbitMqWriteOutcome> WriteAsync(EtlRabbitMqRouteDelivery delivery, CancellationToken cancellationToken)
    {
        var messages = Messages(delivery);
        EtlRabbitMqPublishOutcome outcome;
        try
        {
            outcome = await publisher.PublishAsync(delivery.ConnectionString, delivery.Exchange, delivery.RoutingKey, messages,
                cancellationToken);
        }
        catch (EtlEffectException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return publisher.Classify(exception) == EtlRabbitMqErrorKind.Transient
                ? EtlRabbitMqWriteOutcome.Yield
                : throw new EtlEffectException("etl.rabbitmq-publish-rejected", "The RabbitMQ broker refused the batch.", exception);
        }

        return outcome switch
        {
            EtlRabbitMqPublishOutcome.Confirmed => EtlRabbitMqWriteOutcome.Confirmed,
            // The broker could not take a message now, for example a queue at its length limit; the whole batch is retried.
            EtlRabbitMqPublishOutcome.Nacked => EtlRabbitMqWriteOutcome.Yield,
            EtlRabbitMqPublishOutcome.Unroutable => throw new EtlEffectException("etl.rabbitmq-unroutable",
                "The RabbitMQ broker returned a message because no queue matched its exchange and routing key."),
            _ => throw new EtlEffectException("etl.rabbitmq-publish-rejected", "The RabbitMQ publication outcome is unknown.")
        };
    }

    /// <summary>One message per record of the retained JSON array, each carrying the record's JSON object as its body.</summary>
    internal static IReadOnlyList<EtlRabbitMqMessage> Messages(EtlRabbitMqRouteDelivery delivery)
    {
        try
        {
            using var document = JsonDocument.Parse(delivery.Body);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("A RabbitMQ route body is a JSON array.");
            var messages = new List<EtlRabbitMqMessage>(document.RootElement.GetArrayLength());
            foreach (var record in document.RootElement.EnumerateArray())
            {
                if (record.ValueKind != JsonValueKind.Object)
                    throw new JsonException("A RabbitMQ route record is a JSON object.");
                var index = messages.Count.ToString(CultureInfo.InvariantCulture);
                messages.Add(new EtlRabbitMqMessage(delivery.EffectId + ":" + index,
                [
                    new(RunIdHeader, delivery.RunId),
                    new(BatchIdHeader, delivery.BatchId),
                    new(EffectIdHeader, delivery.EffectId),
                    new(RecordIndexHeader, index)
                ], JsonMarshal.GetRawUtf8Value(record).ToArray()));
            }
            return messages;
        }
        catch (JsonException exception)
        {
            throw new EtlEffectException("etl.connector-output-invalid", "The retained RabbitMQ route body is invalid.", exception);
        }
    }
}
