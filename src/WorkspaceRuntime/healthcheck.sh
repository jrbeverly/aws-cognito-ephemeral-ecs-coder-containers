#!/bin/bash
curl -fsS --max-time 3 http://localhost:7080/healthz 2>/dev/null
