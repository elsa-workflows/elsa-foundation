# Elsa.Etl.RabbitMq

The RabbitMQ publisher for ETL message delivery with publisher confirms ([spec 1200](../../../../specs/1200-etl-rabbitmq-delivery/spec.md)). The feature registers one `IEtlRabbitMqPublisher` over RabbitMQ.Client 7; the ETL runtime owns the delivery protocol (one persistent JSON message per record, a stable message id and effect headers, and recording a batch only once every message is confirmed) and resolves this publisher for `EtlRuntimeFeature.RabbitMqBindings`.

- **Feature:** `EtlRabbitMq`, which depends on `EtlRuntime`. It registers no event handlers, contributes no fan-in events, and registers no tasks.
- **Publication:** each batch opens its own connection, without automatic recovery, and one channel with publisher confirmation tracking, so every publish completes only once the broker confirmed it. A `basic.return` (no queue matched the mandatory message) reports the batch as unroutable; a negative acknowledgement reports it as nacked, which the runtime retries.
- **Failures:** an unreachable broker, a closed connection or channel, an I/O error or a timeout is transient. Failed authentication and a channel or connection closed with access refused (403), not found (404), precondition failed (406) or not allowed (530) are rejections, except a 530 reporting a virtual host's or user's connection limit, which is transient. Anything else is a rejection.
- **Targets:** `DescribeTarget` returns the host, port (5672, or 5671 for `amqps`), virtual host and user name of the AMQP URI, never the password.
