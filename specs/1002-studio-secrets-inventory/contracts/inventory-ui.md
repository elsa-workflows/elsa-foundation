# Inventory UI contract

- Keep the existing search input/Search/Enter and local facets; do not introduce global facet or paging semantics.
- Explain displayed matches within loaded metadata and the total of the completed server search. An unsubmitted draft cannot relabel the observation.
- Show partial coverage/search help only for actual incompleteness. First100 is a request boundary, not a complete-catalog assertion or proof of missing rows.
- Scope choices retain visible and accessible loaded-results wording; no complete-catalog claim from registered descriptors.
- Local-facet empty text is bounded to loaded rows. Complete server empty/no-match, loading and errors are distinct.
- Existing detail opens the exact metadata record and closes by normal controls/Escape with criteria/focus retained. No secret values/configuration keys or new API request type are introduced.
- Latest request/context owns items, total, loading/error and relevant selection reconciliation; stale results cannot overwrite another observation.
- A selected scope absent from current loaded metadata stays visibly selected with a bounded qualifier; choosing All clears it. No descriptor is promoted into a complete catalog.
- Refresh and post-mutation list reloads retain the accepted query; failed-load Retry repeats the attempted query. Neither applies later draft text. Search/Enter remains the explicit draft submission.
- Authoritative zero-result search wording takes precedence over local-facet empty wording when both conditions apply.
- Existing backend search boundary above10,000 remains an honest server error. No claim that it is fixed.
