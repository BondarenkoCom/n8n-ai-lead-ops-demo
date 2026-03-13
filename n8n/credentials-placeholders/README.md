# Credential placeholders

The workflow is designed to run in local mock mode without importing external credentials.

If you want to bind live connectors later, create n8n credentials for:

- HTTP access to the mock backend or a real CRM API
- Slack incoming webhook
- Gmail or Microsoft 365 mail draft connector
- OpenAI-compatible API access if you move LLM calls into n8n credentials instead of environment variables

This portfolio sample keeps all credential material out of the repository.
