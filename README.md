# IA API

API ASP.NET Core para funcionalidades de IA com foco em RAG (Retrieval-Augmented Generation):
- upload e indexação de documentos (`.md` e `.pdf`),
- busca semântica de chunks no Elasticsearch,
- perguntas com contexto (RAG) usando OpenAI,

<img width="1875" height="867" alt="image" src="https://github.com/user-attachments/assets/a83012a1-871f-4fc2-a21d-fd92688f0810" />


## O que o projeto faz

Principais capacidades:
- **Gerenciamento de documentos por tema**
  - upload síncrono e em fila,
  - listagem e remoção de documentos,
  - separação por tema (índices temáticos no Elasticsearch).
- **RAG para perguntas**
  - gera embedding da pergunta,
  - recupera chunks relevantes,
  - expande contexto com vizinhos de chunk,
  - monta resposta com citações.
- **Observabilidade e saúde**
  - logs com Serilog,
  - endpoint de health check.

## Stack tecnológica

- **.NET / ASP.NET Core**: `net10.0`
- **OpenAI API**: geração de embeddings e respostas
- **Elasticsearch**: armazenamento e busca vetorial/textual dos documentos/chunks
- **Parsers de documento**:
  - Markdig (Markdown)
  - UglyToad.PdfPig (PDF)
- **Tokenização**: SharpToken
- **Documentação de API**:
  - Microsoft.AspNetCore.OpenApi
  - Scalar.AspNetCore (`/doc`)
- **Logging**: Serilog

## Estrutura (resumo)

- `API/Controllers`: endpoints HTTP
- `API/Application`: casos de uso (upload, perguntas, busca de chunks)
- `API/Infrastructure`: integrações (OpenAI, Elasticsearch, parsers, chunking)
- `API/Domain/Entities`: contratos e modelos
- `API/Program.cs`: composição da aplicação e DI

## Endpoints principais

Base local padrão: `http://localhost:6080`

- `POST /assistent/documents`: Faz o upload de um documento e processa o conteúdo imediatamente para indexação no tema informado.​
- `POST /assistent/documents/queue`: Enfileira um documento para processamento assíncrono, evitando duplicidade na fila por tema.
- `GET /assistent/documents`: Lista os documentos indexados, com opção de filtrar por tema.​
- `GET /assistent/documents/{id}`: Lista os itens de upload que ainda estão pendentes na fila de processamento.​
- `DELETE /assistent/documents/{id}?theme={theme}`: Remove um documento de um tema específico.
- `GET /assistent/documents/queue/pending`: Lista os itens de upload que ainda estão pendentes na fila de processamento.
- `POST /assistent/questions`: Processa uma pergunta no contexto RAG e retorna a resposta gerada pela IA.​
- `POST /assistent/questions/chunks`: Busca os chunks mais relevantes para uma pergunta sem gerar resposta final.​
- `GET /assistent/themes`: Lista os temas cadastrados para organização e consulta dos documentos.
- `GET /health`
- `GET /doc` (documentação interativa)

## Exemplos de uso (curl)

Defina variáveis para facilitar:

```bash
BASE_URL="http://localhost:6080"
API_KEY="seu_token_aqui"
THEME="juridico"
```

### Health check
```bash
curl -X GET "$BASE_URL/health"
```

### Listar temas
```bash
curl -X GET "$BASE_URL/assistent/themes" \
  -H "Authorization: Bearer $API_KEY"
```

### Upload de documento (markdown/pdf)
```bash
curl -X POST "$BASE_URL/assistent/documents" \
  -H "Authorization: Bearer $API_KEY" \
  -F "theme=$THEME" \
  -F "file=@./arquivo.md"
```

### Enfileirar upload
```bash
curl -X POST "$BASE_URL/assistent/documents/queue" \
  -H "Authorization: Bearer $API_KEY" \
  -F "theme=$THEME" \
  -F "file=@./arquivo.pdf"
```

### Listar documentos por tema
```bash
curl -X GET "$BASE_URL/assistent/documents?theme=$THEME" \
  -H "Authorization: Bearer $API_KEY"
```

### Buscar documento por ID
```bash
DOCUMENT_ID="id_do_documento"

curl -X GET "$BASE_URL/assistent/documents/$DOCUMENT_ID" \
  -H "Authorization: Bearer $API_KEY"
```

### Remover documento
```bash
DOCUMENT_ID="id_do_documento"

curl -X DELETE "$BASE_URL/assistent/documents/$DOCUMENT_ID?theme=$THEME" \
  -H "Authorization: Bearer $API_KEY"
```

### Perguntar com RAG
```bash
curl -X POST "$BASE_URL/assistent/questions" \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
	"question": "Quais cláusulas tratam de multa?",
	"theme": "juridico",
	"topK": 5
  }'
```

### Buscar chunks para inspeção
```bash
curl -X POST "$BASE_URL/assistent/questions/chunks" \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
	"question": "Quais cláusulas tratam de multa?",
	"theme": "juridico",
	"topK": 5
  }'
```

## Integração com OpenWebUI (RAG externo)

É possível integrar o endpoint `POST /assistent/questions/chunks` ao **OpenWebUI** (https://openwebui.com/) para usar esta API como camada de recuperação (RAG) da aplicação.

Fluxo sugerido:
- o OpenWebUI envia a pergunta e o tema para `POST /assistent/questions/chunks`;
- a API retorna os chunks relevantes (conteúdo e metadados);
- o OpenWebUI injeta esse contexto no prompt final do modelo configurado nele.

Exemplo de chamada do endpoint de chunks:

```bash
curl -X POST "$BASE_URL/assistent/questions/chunks" \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
	"question": "Explique os principais pontos do documento.",
	"theme": "juridico",
	"topK": 5
  }'
```

Com isso, você pode manter indexação/recuperação nesta API e usar o OpenWebUI como interface de chat. A integração pode ocorrer via Tools ou Filters disponíveis pelo OpenWebUI:
<img width="898" height="620" alt="image" src="https://github.com/user-attachments/assets/6acaf771-b078-4d41-af13-b2ff091a4fb0" />

<img width="1359" height="522" alt="image" src="https://github.com/user-attachments/assets/e052a30c-15c5-4930-827b-ec1f4b2b9170" />





## Como rodar

### Pré-requisitos
- SDK **.NET 10**
- Acesso a um Elasticsearch
- Chave da OpenAI válida

### 1) Configurar a aplicação
Configure `API/appsettings.Development.json` (ou variáveis de ambiente) com:
- `OpenAI:ApiKey`
- `OpenAI:BaseUrl`
- `OpenAI:ChatModel`
- `OpenAI:EmbeddingModel`
- `Elasticsearch:Url`
- `Elasticsearch:ApiKey` (ou usuário/senha)
- `KestrelUrl` (padrão: `http://localhost:6080`)

> Recomendado: não versionar segredos em arquivo. Use User Secrets ou variáveis de ambiente.

### 2) Restaurar pacotes
```bash
dotnet restore API/IA.API.csproj
```

### 3) Executar
```bash
dotnet run --project API/IA.API.csproj
```

### 4) Acessar
- Documentação: `http://localhost:6080/doc`
- Health: `http://localhost:6080/health`

## Observações

- O projeto registra `DocumentUploadQueueHostedService` para processar uploads em fila.
- Em ambiente de desenvolvimento, a validação de API key no atributo customizado é relaxada para alguns cenários.
- O limite de upload foi configurado para até 500MB no servidor; as regras funcionais de upload também dependem de `Upload` nas configurações.
- Há um arquivo `API/rag-feature.http` com exemplos adicionais para testes locais via cliente HTTP.
