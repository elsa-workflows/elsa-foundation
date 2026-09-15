#!/usr/bin/env bash
# Rewrites the provider-package calls EF emits into generated migrations so the module assembly
# compiles against EF Core + Relational only. Usage: strip-provider-calls.sh <migrations-dir>
set -euo pipefail
dir="$1"
for file in "$dir"/*.cs; do
  sed -i.bak -E \
    -e 's#^([[:space:]]*)SqlServerModelBuilderExtensions\.UseIdentityColumns\(modelBuilder\);#\1// SqlServerModelBuilderExtensions.UseIdentityColumns omitted: the module stays provider-free.#' \
    -e 's#^([[:space:]]*)NpgsqlModelBuilderExtensions\.UseIdentityByDefaultColumns\(modelBuilder\);#\1// NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns omitted: the module stays provider-free.#' \
    -e 's#^([[:space:]]*)MySQLModelBuilderExtensions\.HasCharSet\(modelBuilder, "([^"]+)"\);#\1modelBuilder.HasAnnotation("MySQL:Charset", "\2");#' \
    -e '/^using (Npgsql|MySql|Microsoft\.EntityFrameworkCore\.SqlServer|Microsoft\.EntityFrameworkCore\.Sqlite|MySql\.EntityFrameworkCore)[A-Za-z.]*;$/d' \
    "$file"
  rm -f "$file.bak"
done
