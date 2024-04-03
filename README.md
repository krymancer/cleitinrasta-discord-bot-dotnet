# DJ Cleiton Rasta

Este é um bot do discord criado com a finalidade de entender como funciona a criação de bots utilizando a blibioteca Discord.Net.
Esse bot foi criado devido a morte do Groovy, um bot de música excelente que utilizei durante muito tempo, e ainda hoje não achei nenhum bot que substitua ele, então decidi criar o meu próprio bot de música.

Lógico que mesmo este está longe de subistituir o Groovy e mal tem funcionalidade basicas, mas conforme o aprendizado for aumentando, mais funcionalidades serão adicionadas.

## Configuração do bot

Para poder utilizar o bot é necessário editar o arquivo `appsettings.json` na pasta `src/ConsoleApp`, o arquivo deve seguir o seguinte exemplo:

```json
{
  "Discord": {
    "Token": "SEU TOKEN DO BOT DO DISCORD, pegue na aba de bot do seu bot no discord developer portal",
    "Prefix" : "SEU Prefixo, pode ser qualquer coisa, recomendo que evite prefixes de outros bots, eu costumo usar &"
  }
}
```
