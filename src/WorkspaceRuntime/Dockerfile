ARG CODE_SERVER_VERSION=4.96.4
FROM codercom/code-server:${CODE_SERVER_VERSION}

USER root
RUN mkdir -p /workspace && chown coder:coder /workspace
COPY healthcheck.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/healthcheck.sh

ENV HOME=/workspace
WORKDIR /workspace
USER coder
EXPOSE 7080

HEALTHCHECK --interval=5s --timeout=3s --start-period=30s --retries=3 \
    CMD /usr/local/bin/healthcheck.sh || exit 1

# Cognito on the ALB is the only auth; code-server itself is open.
ENTRYPOINT ["/usr/bin/entrypoint.sh", "--auth", "none", "--bind-addr", "0.0.0.0:7080", "/workspace"]
