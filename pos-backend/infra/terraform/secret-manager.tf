# ---------------------------------------------------------------------------
# secret-manager.tf — Secret Manager secrets for CloudHub runtime config
# ---------------------------------------------------------------------------

# Random 64-character value used as JWT signing key placeholder.
# Replace this secret version manually with a production key before first deploy.
resource "random_password" "jwt_signing_key" {
  length  = 64
  special = false
}

# --- Database connection string -------------------------------------------

resource "google_secret_manager_secret" "db_connection_string" {
  secret_id = "pos-db-connection-string"

  replication {
    auto {}
  }

  labels = {
    environment = var.environment
    managed_by  = "terraform"
  }
}

# Placeholder value — update manually after Cloud SQL instance is provisioned
# and the real connection string is known.
resource "google_secret_manager_secret_version" "db_connection_string_placeholder" {
  secret      = google_secret_manager_secret.db_connection_string.id
  secret_data = "Host=REPLACE_ME;Port=5432;Database=pos_cloud;Username=pos_app;Password=REPLACE_ME;SSL Mode=Require"

  lifecycle {
    # Prevent Terraform from overwriting a secret that has been set manually.
    ignore_changes = [secret_data]
  }
}

# --- JWT signing key -------------------------------------------------------

resource "google_secret_manager_secret" "jwt_signing_key" {
  secret_id = "pos-jwt-signing-key"

  replication {
    auto {}
  }

  labels = {
    environment = var.environment
    managed_by  = "terraform"
  }
}

resource "google_secret_manager_secret_version" "jwt_signing_key_initial" {
  secret      = google_secret_manager_secret.jwt_signing_key.id
  secret_data = random_password.jwt_signing_key.result

  lifecycle {
    # Prevent rotation from being reverted on the next terraform apply.
    ignore_changes = [secret_data]
  }
}

# --- IAM: grant Cloud Run SA access to both secrets -----------------------

resource "google_secret_manager_secret_iam_member" "cloudhub_sa_db_connection_string" {
  secret_id = google_secret_manager_secret.db_connection_string.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.cloudhub_sa.email}"
}

resource "google_secret_manager_secret_iam_member" "cloudhub_sa_jwt_signing_key" {
  secret_id = google_secret_manager_secret.jwt_signing_key.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.cloudhub_sa.email}"
}
