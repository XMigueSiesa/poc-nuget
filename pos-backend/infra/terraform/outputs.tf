# ---------------------------------------------------------------------------
# outputs.tf — values needed by CI/CD and operators
# ---------------------------------------------------------------------------

output "cloud_run_url" {
  description = "Public URL of the CloudHub Cloud Run service"
  value       = google_cloud_run_v2_service.pos_cloudhub.uri
}

output "cloud_sql_connection_name" {
  description = "Cloud SQL instance connection name (project:region:instance)"
  value       = google_sql_database_instance.pos_db.connection_name
}

output "artifact_registry_url" {
  description = "Docker registry URL for pushing images"
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.pos_images.repository_id}"
}
