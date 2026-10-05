# NeoTasks web

Interface React do [NeoTasks](../README.md), adaptada de [ingacode-test-frontend](https://github.com/LucianoNeo/ingacode-test-frontend).

Para avaliar a aplicação completa, execute `docker compose up --build` na raiz do repositório. O Nginx encaminha `/app-api` para a API .NET. Cadastro, login, projetos, tarefas, equipe e apontamentos usam esse contrato.

O build usa Node 22, React 18, TypeScript, Vite e Tailwind. Testes de navegador ficam em `e2e/` e são executados pelo GitHub Actions.

O antigo serviço Fastify e a URL externa da aplicação original não são usados por esta versão.
