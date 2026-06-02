# Copilot Instructions

## Project Guidelines
- All code, comments, strings, messages, and tooltips must be in English only. No Bulgarian text in code.
- Conversation language with this user can be in Bulgarian.
- When modifying this project, prioritize incremental improvements that preserve existing behavior and avoid breaking working functionality.
- When a concept and implementation plan are agreed, continue executing the approved plan without asking for confirmation at every step; keep progress updates concise and proceed until completion unless blocked.
- Do not set AI request temperature to non-default values for this project; use the provider/model default or 1.0 when a DevExpress AI behavior requires a temperature value because some configured models reject values like 0.3.

## Database Tools Guidelines
- Prefer the new guarded database insight approach as the primary database evidence path.
- Avoid over-restricting the result limit of the database insights.
- Use legacy generic query tools only as a fallback or for explicit record/table requests.
- Skip proposed AI agent improvements 7 (DBChat explain/validate without executing) and 9 (tool call budget/loop guard) for now. When implementing current context tooling, avoid confusing the custom chat window itself with the user's active XAF application context.