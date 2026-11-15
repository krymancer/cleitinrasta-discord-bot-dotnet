# `/live` — sala para compartilhar tela

`/live` cria uma sala de videochamada e manda o link no canal.

- **Sem configuração:** cria uma sala aleatória no [Jitsi Meet](https://meet.jit.si) (grátis, sem conta).
  O primeiro a entrar pode precisar logar com Google/GitHub para ser moderador.
- **Com Google Meet configurado:** cria uma sala nova no Google Meet pela API oficial, com acesso
  `OPEN` (qualquer pessoa com o link entra sem pedir permissão, quando a conta deixa).
  Se a API do Meet falhar, o bot cai para o Jitsi.

## Configurar o Google Meet (uma vez só)

As salas ficam no nome de uma conta Google (pode ser um Gmail normal).

1. No [Google Cloud Console](https://console.cloud.google.com), crie um projeto e ative a
   **Google Meet REST API** (APIs e serviços → Biblioteca).
2. **Tela de consentimento OAuth**: tipo *Externo*, adicione o escopo
   `https://www.googleapis.com/auth/meetings.space.created` e coloque a conta como usuário de teste.
   Depois clique em **Publicar app** (status *Em produção*). Em modo *Teste* o refresh token
   expira em 7 dias. O app não precisa ser verificado para uso pessoal.
3. **Credenciais → Criar credenciais → ID do cliente OAuth**, tipo *Aplicativo da Web*, com o URI
   de redirecionamento `https://developers.google.com/oauthplayground`. Guarde o client id e o secret.
4. Abra o [OAuth Playground](https://developers.google.com/oauthplayground):
   - Engrenagem ⚙️ → marque **Use your own OAuth credentials** e cole o client id/secret.
   - No campo de escopo, digite `https://www.googleapis.com/auth/meetings.space.created` → **Authorize APIs**
     e entre com a conta que vai ser dona das salas.
   - **Exchange authorization code for tokens** → copie o **Refresh token**.
5. Passe as variáveis para o bot:

| Variável (env)              | Chave no `.env` / Secret     |
|-----------------------------|------------------------------|
| `GoogleMeet__ClientId`      | `GOOGLE_MEET_CLIENT_ID`      |
| `GoogleMeet__ClientSecret`  | `GOOGLE_MEET_CLIENT_SECRET`  |
| `GoogleMeet__RefreshToken`  | `GOOGLE_MEET_REFRESH_TOKEN`  |

No Kubernetes, adicione as três chaves ao Secret `discord-bot-secret`:

```sh
kubectl -n discord-bot patch secret discord-bot-secret --type merge -p \
  '{"stringData":{"GOOGLE_MEET_CLIENT_ID":"...","GOOGLE_MEET_CLIENT_SECRET":"...","GOOGLE_MEET_REFRESH_TOKEN":"..."}}'
kubectl -n discord-bot rollout restart deployment/discord-bot
```
