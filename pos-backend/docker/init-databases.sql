-- Create databases for POS POC
-- pos_local: used by LocalHub (all modules)
-- pos_cloud: used by Cloud.Products (products only)

SELECT 'CREATE DATABASE pos_local'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'pos_local')\gexec

SELECT 'CREATE DATABASE pos_cloud'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'pos_cloud')\gexec
