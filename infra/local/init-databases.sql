-- PharmaFlow Multi-Service Database Initialization Script
-- Executed on initial PostgreSQL container startup
-- Creates isolated databases and isolated service roles per bounded context

-- 1. Create Service Roles with Restricted Passwords (configurable in production)
CREATE ROLE identity_user WITH LOGIN PASSWORD 'IdentitySecretPass123!';
CREATE ROLE operations_user WITH LOGIN PASSWORD 'OperationsSecretPass123!';
CREATE ROLE commerce_user WITH LOGIN PASSWORD 'CommerceSecretPass123!';
CREATE ROLE workforce_user WITH LOGIN PASSWORD 'WorkforceSecretPass123!';
CREATE ROLE documents_user WITH LOGIN PASSWORD 'DocumentsSecretPass123!';
CREATE ROLE reporting_user WITH LOGIN PASSWORD 'ReportingSecretPass123!';

-- 2. Create Isolated Databases
CREATE DATABASE pharmaflow_identity OWNER identity_user;
CREATE DATABASE pharmaflow_operations OWNER operations_user;
CREATE DATABASE pharmaflow_commerce OWNER commerce_user;
CREATE DATABASE pharmaflow_workforce OWNER workforce_user;
CREATE DATABASE pharmaflow_documents OWNER documents_user;
CREATE DATABASE pharmaflow_reporting OWNER reporting_user;

-- 3. Grant Permissions within respective databases
\connect pharmaflow_identity
GRANT ALL PRIVILEGES ON DATABASE pharmaflow_identity TO identity_user;
GRANT ALL ON SCHEMA public TO identity_user;

\connect pharmaflow_operations
GRANT ALL PRIVILEGES ON DATABASE pharmaflow_operations TO operations_user;
GRANT ALL ON SCHEMA public TO operations_user;

\connect pharmaflow_commerce
GRANT ALL PRIVILEGES ON DATABASE pharmaflow_commerce TO commerce_user;
GRANT ALL ON SCHEMA public TO commerce_user;

\connect pharmaflow_workforce
GRANT ALL PRIVILEGES ON DATABASE pharmaflow_workforce TO workforce_user;
GRANT ALL ON SCHEMA public TO workforce_user;

\connect pharmaflow_documents
GRANT ALL PRIVILEGES ON DATABASE pharmaflow_documents TO documents_user;
GRANT ALL ON SCHEMA public TO documents_user;

\connect pharmaflow_reporting
GRANT ALL PRIVILEGES ON DATABASE pharmaflow_reporting TO reporting_user;
GRANT ALL ON SCHEMA public TO reporting_user;
