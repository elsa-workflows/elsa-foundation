import React from "react";
import type { SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";

export function SecretDetail({ secret, descriptors }: { secret: SecretMetadata | null; descriptors: SecretDescriptorsResponse | null }) {
  if (!secret) {
    return <section className="secrets-detail secrets-empty">Select a secret.</section>;
  }

  const store = descriptors?.stores.find(x => x.name === secret.storeName);
  const type = descriptors?.types.find(x => x.name === secret.typeName);

  return (
    <section className="secrets-detail">
      <header>
        <h3>{secret.displayName || secret.name}</h3>
        <span>{secret.status}</span>
      </header>
      <dl>
        <div><dt>Name</dt><dd>{secret.name}</dd></div>
        <div><dt>Type</dt><dd>{type?.displayName ?? secret.typeName}</dd></div>
        <div><dt>Store</dt><dd>{store?.displayName ?? secret.storeName}</dd></div>
        <div><dt>Version</dt><dd>{secret.currentVersion ?? "none"}</dd></div>
        <div><dt>Created</dt><dd>{formatDate(secret.createdAt)}</dd></div>
        <div><dt>Expires</dt><dd>{secret.expiresAt ? formatDate(secret.expiresAt) : "never"}</dd></div>
      </dl>
      {secret.description ? <p>{secret.description}</p> : null}
      <div className="secrets-redaction">Values are never displayed.</div>
    </section>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}
