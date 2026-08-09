# Security & File-Safety Gates

Before merging any mutation/public-surface sprint, review:

## Local API
- Host allowed?
- Origin allowed?
- startup session secret required?
- no state-changing GET?
- no wildcard CORS?

## Filesystem
- canonical path?
- root containment?
- reparse point?
- basename vs path distinction?
- source fingerprint checked?

## Mutation
- previewed?
- recovery capacity verified?
- snapshot created?
- temp mutation?
- validation?
- atomic replace?
- reindex afterward?

## Sharing
- explicit ShareMember?
- no local path payload?
- token scoped?
- fixed-time verification?
- download permission server-side?
- no privileged dependencies in ShareHost?
- CSP/sanitizer/rate limit?
