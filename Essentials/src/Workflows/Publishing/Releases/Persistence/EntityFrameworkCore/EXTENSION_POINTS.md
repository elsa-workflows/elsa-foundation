# Consumer context composition

`AddReleaseArchiveModel` maps only archive tables in a consumer-owned context; `AddReleaseArchiveStore<TContext>` selects
one scoped implementation of the archive reader, writer and capability ports. Hosts retain provider/options, schema and
migration ownership. Custom codecs are resolved through the existing `IReleaseBundleCodec` port; this package adds no
transport codec, authorization framework, module startup or owner catalog dependency. `requireDurable` defaults to true.
