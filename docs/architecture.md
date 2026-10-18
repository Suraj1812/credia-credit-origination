# Architecture notes

The domain layer contains entities and deterministic credit policy functions. The application layer defines contracts, validated inputs, and integration interfaces. Infrastructure implements EF Core persistence, workflow rules, document extraction, bureau adapters, and an outbox dispatcher. Web exposes REST routes and Blazor pages over the same scoped workspace service.

A case has a monotonic version. Each mutation requires the caller's observed version and the database column is an EF concurrency token. Audit and outbox records are inserted in the same save operation as the case change. The outbox dispatcher can retry delivery; downstream consumers must deduplicate by message ID.

The document flow stores original bytes and a SHA-256 digest. A TXT sample can be parsed locally; Azure Document Intelligence is the optional external extractor. Extraction alone does not update the applicant's income. A human must confirm a matching ID and valid amount. Any data change invalidates a previous evaluation because its application version no longer matches.

The score adapter is synthetic only in Development with demo mode enabled. All other environments use an unconfigured adapter that refuses evaluation. The signature step records an intent in the workflow and never represents a completed legal signature.
