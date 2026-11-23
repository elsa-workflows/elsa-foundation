#!/usr/bin/env bash
# Release jobs run on Linux x64. Keep this verifier independent of mutable installer scripts.
set -euo pipefail
dest=$(mktemp -d "$RUNNER_TEMP/elsa-image-tools.XXXXXX")
archive="$dest/oras.tar.gz"
curl --fail --location --retry 3 --output "$archive" \
  https://github.com/oras-project/oras/releases/download/v1.4.0/oras_1.4.0_linux_amd64.tar.gz
echo "d8e1dce2fd8ce996f1d901845b688eb0cf505a6082897606f053825bb47935d1  $archive" | sha256sum --check --status
tar -xzf "$archive" -C "$dest" oras
echo "$dest" >> "$GITHUB_PATH"

validator="$dest/jsonschema.zip"
curl --fail --location --retry 3 --output "$validator" \
  https://github.com/sourcemeta/jsonschema/releases/download/v17.3.3/jsonschema-17.3.3-linux-x86_64.zip
echo "2b2f91d5b429ee8f440d11c0413a24d6acc1406d209989fb618e39f4ff1157ed  $validator" | sha256sum --check --status
unzip -qj "$validator" '*/bin/jsonschema' -d "$dest"

curl --fail --location --retry 3 --output "$dest/spdx-schema.json" \
  https://raw.githubusercontent.com/spdx/spdx-spec/6f2cb47d13db19b12c23885573b22ef32ec1ada5/schemas/spdx-schema.json
echo "ELSA_IMAGE_SPDX_SCHEMA=$dest/spdx-schema.json" >> "$GITHUB_ENV"
