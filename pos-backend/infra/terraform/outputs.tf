# ---------------------------------------------------------------------------
# outputs.tf — Exported values from SIESA POS infrastructure
# ---------------------------------------------------------------------------

output "cloud_run_url" {
  description = "Public URL of the CloudHub Cloud Run service"
  value       = google_cloud_run_v2_service.cloudhub.uri
}

output "cloud_sql_connection_name" {
  description = "Cloud SQL instance connection name for use with Cloud SQL Auth Proxy"
  value       = google_sql_database_instance.pos.connection_name
}

output "artifact_registry_url" {
  description = "Artifact Registry repository URL for pushing container images"
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.pos_containers.repository_id}"
}

output "service_account_email" {
  description = "Email of the Cloud Run service account"
  value       = google_service_account.cloudhub_sa.email
}
