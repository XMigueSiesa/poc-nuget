# ---------------------------------------------------------------------------
# iam.tf — Service account and project-level IAM bindings for CloudHub
# ---------------------------------------------------------------------------

resource "google_service_account" "cloudhub_sa" {
  account_id   = "pos-cloudhub-sa"
  display_name = "SIESA POS CloudHub Service Account"
  description  = "Identity used by the CloudHub Cloud Run service to access GCP resources"
}

locals {
  cloudhub_sa_roles = [
    "roles/cloudsql.client",
    "roles/secretmanager.secretAccessor",
    "roles/logging.logWriter",
    "roles/cloudtrace.agent",
    "roles/monitoring.metricWriter",
  ]
}

resource "google_project_iam_member" "cloudhub_sa_roles" {
  for_each = toset(local.cloudhub_sa_roles)

  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.cloudhub_sa.email}"
}
