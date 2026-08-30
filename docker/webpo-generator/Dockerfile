# Pin of ashton045/webpo-generator @ ca301fd (companion to youtube-source PR 229).
FROM node:24-bookworm-slim

ARG WEBPO_SHA=ca301fd89d2bcbe3bbe423d5fd3deba9b337d75a

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && curl -fsSL "https://github.com/ashton045/webpo-generator/archive/${WEBPO_SHA}.tar.gz" \
      | tar xz --strip-components=1 \
    && npm ci --omit=dev \
    && apt-get purge -y curl \
    && rm -rf /var/lib/apt/lists/* /tmp/*

ENV HOST=0.0.0.0
ENV PORT=8080
EXPOSE 8080
USER node
CMD ["node", "server.js"]
