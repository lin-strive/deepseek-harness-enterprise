FROM eclipse-temurin:21-jre-jammy

WORKDIR /app
COPY app.jar /app/app.jar

EXPOSE 8080
USER 10001
ENTRYPOINT ["java", "-XX:MaxRAMPercentage=70", "-jar", "/app/app.jar"]
