# ---------------------------------------------------------------------------
# iam.tf — service account and role bindings for Cloud Run
# ---------------------------------------------------------------------------

resource "google_service_account" "cloudhub" {
  account_id   = "pos-cloudhub"
  display_name = "POS CloudHub Cloud Run"
  description  = "Service account for the pos-cloudhub Cloud Run service"
}

locals {
  cloudhub_roles = [
    "roles/cloudsql.client",
    "roles/secretmanager.secretAccessor",
    "roles/logging.logWriter",
    "roles/cloudtrace.agent",
    "roles/monitoring.metricWriter",
  ]
}

resource "google_project_iam_member" "cloudhub_roles" {
  for_each = toset(local.cloudhub_roles)

  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.cloudhub.email}"
}
