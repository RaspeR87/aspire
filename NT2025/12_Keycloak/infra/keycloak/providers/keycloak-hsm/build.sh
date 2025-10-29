#!/bin/bash
set -euo pipefail

# Define variables
IMAGE="maven:3.9.9-eclipse-temurin-17"
SRC_DIR="$(pwd)"
TARGET_DIR="$SRC_DIR/target"

# Run Maven inside a container with proper compiler options
docker run --rm \
    -v "$SRC_DIR":/app \
    -w /app \
    $IMAGE mvn clean package \
    -DadditionalCompilerArgs="--add-exports java.base/sun.security.pkcs11=ALL-UNNAMED --add-modules jdk.crypto.cryptoki" \
    -Dmaven.compiler.compilerArgs="--add-exports java.base/sun.security.pkcs11=ALL-UNNAMED --add-modules jdk.crypto.cryptoki"

# Debug: show what's in target
echo "---- target/ contents ----"
ls -l "$TARGET_DIR" || true
echo "--------------------------"

# Copy built JAR to deploy directory
mkdir -p "$TARGET_DIR/deploy"

JAR_FILE=$(find "$TARGET_DIR" -maxdepth 1 -name "keycloak-hsm-provider-*.jar" | head -n 1)

if [[ -n "${JAR_FILE:-}" && -f "$JAR_FILE" ]]; then
  cp "$JAR_FILE" "$TARGET_DIR/deploy/"
  echo "✅ Built: $(basename "$JAR_FILE")"
  echo "➡  Copied to: $TARGET_DIR/deploy/"
else
  echo "❌ JAR not found. Check the Maven output above."
  exit 1
fi
