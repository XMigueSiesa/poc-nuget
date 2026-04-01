# ---------------------------------------------------------------------------
# cloud-run.tf — Cloud Run service for Pos.Host.CloudHub
# ---------------------------------------------------------------------------

resource "google_cloud_run_v2_service" "pos_cloudhub" {
  name     = "pos-cloudhub"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_ALL"

  labels = {
    environment = var.environment
    managed_by  = "terraform"
  }

  template {
    service_account = google_service_account.cloudhub.email

    scaling {
      min_instance_count = var.min_instances
      max_instance_count = var.max_instances
    }

    vpc_access {
      connector = google_vpc_access_connector.pos_connector.id
      egress    = "PRIVATE_RANGES_ONLY"
    }

    volumes {
      name = "cloudsql"
      cloud_sql_instance {
        instances = [google_sql_database_instance.pos_db.connection_name]
      }
    }

    containers {
      image = var.cloudhub_image

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
        cpu_idle = true
      }

      env {
        name = "ConnectionStrings__CloudDb"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.db_connection_string.secret_id
            version = "latest"
          }
        }
      }

      env {
        name = "Auth__JwtSigningKey"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.jwt_signing_key.secret_id
            version = "latest"
          }
        }
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      volume_mounts {
        name       = "cloudsql"
        mount_path = "/cloudsql"
      }

      startup_probe {
        http_get {
          path = "/health/ready"
          port = 8080
        }
        initial_delay_seconds = 5
        period_seconds        = 5
        failure_threshold     = 10
        timeout_seconds       = 3
      }

      liveness_probe {
        http_get {
          path = "/health/live"
          port = 8080
        }
        period_seconds    = 30
        failure_threshold = 3
        timeout_seconds   = 3
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.db_connection_string,
    google_secret_manager_secret_version.jwt_signing_key,
    google_secret_manager_secret_iam_member.db_conn_access,
    google_secret_manager_secret_iam_member.jwt_key_access,
  ]
}

# Allow unauthenticated access — the application handles its own auth
resource "google_cloud_run_v2_service_iam_member" "public_invoker" {
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.pos_cloudhub.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}
