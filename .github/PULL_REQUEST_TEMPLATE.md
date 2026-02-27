# Pull Request

For v2.3 release-track PRs, you can use:
`docs/PR-Template-v2.3-Acceptance.md`

## Summary
Describe the changes and their purpose.

## Linked Issue
Closes #<issue-id>

## Validation
- Commands/tests run:
```bash
# paste exact validation commands and key outcomes
```


## TDD Evidence (Required)
- Evidence level: `A (strict test-first)` / `B (co-committed)` / `C (backfill)`
- Red test commit SHA:
- Green implementation commit SHA:
- Refactor commit SHA (optional):
- Test files:
- Implementation files:
- If level is `B` or `C`, explain why strict test-first was not used and link follow-up issue:

## Rollback Notes
Describe rollback plan or why rollback is low risk.

## Testing
- [ ] Tests added/updated
- [ ] Tests run locally

## Checklist
- [ ] Code style and formatting checked
- [ ] Documentation updated (if applicable)
- [ ] Security impact reviewed (auth/secrets/supply-chain)
- [ ] No secrets or credentials committed
- [ ] Breaking changes documented
