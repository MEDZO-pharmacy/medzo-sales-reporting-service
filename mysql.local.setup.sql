CREATE DATABASE IF NOT EXISTS medzo_sales
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE USER IF NOT EXISTS 'medzo_sales_app'@'localhost'
  IDENTIFIED BY 'replace-with-a-strong-local-password';

GRANT ALL PRIVILEGES ON medzo_sales.* TO 'medzo_sales_app'@'localhost';
FLUSH PRIVILEGES;
