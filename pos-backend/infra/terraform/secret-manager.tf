# ---------------------------------------------------------------------------
# secret-manager.tf — secrets for CloudHub
# ---------------------------------------------------------------------------

# Database connection string using Cloud SQL Unix socket
resource "google_secret_manager_secret" "db_connection_string" {
  secret_id = "pos-cloudhub-db-connection-string"

  replication {
    auto {}
  }

  labels = {
    environment = var.environment
    managed_by  = "terraform"
  }
}

resource "google_secret_manager_secret_version" "db_connection_string" {
  secret      = google_secret_manager_secret.db_connection_string.id
  secret_data = "Host=/cloudsql/${google_sql_database_instance.pos_db.connection_name};Database=pos_cloud;Username=pos_app;Password=${random_password.pos_app_password.result}"

  lifecycle {
    ignore_changes = [secret_data]
  }
}

# JWT signing key
resource "random_password" "jwt_signing_key" {
  length  = 64
  special = false
}

resource "google_secret_manager_secret" "jwt_signing_key" {
  secret_id = "pos-cloudhub-jwt-signing-key"

  replication {
    auto {}
  }

  labels = {
    environment = var.environment
    managed_by  = "terraform"
  }
}

resource "google_secret_manager_secret_version" "jwt_signing_key" {
  secret      = google_secret_manager_secret.jwt_signing_key.id
  secret_data = random_password.jwt_signing_key.result

  lifecycle {
    ignore_changes = [secret_data]
  }
}

# Grant the Cloud Run service account access to secrets
resource "google_secret_manager_secret_iam_member" "db_conn_access" {
  secret_id = google_secret_manager_secret.db_connection_string.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.cloudhub.email}"
}

resource "google_secret_manager_secret_iam_member" "jwt_key_access" {
  secret_id = google_secret_manager_secret.jwt_signing_key.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.cloudhub.email}"
}
