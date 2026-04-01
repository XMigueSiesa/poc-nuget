# ---------------------------------------------------------------------------
# artifact-registry.tf — Docker repository for CloudHub container images
# ---------------------------------------------------------------------------

resource "google_artifact_registry_repository" "pos_containers" {
  repository_id = "pos-containers"
  location      = var.region
  format        = "DOCKER"
  description   = "SIESA POS container images"

  labels = {
    environment = var.environment
    managed_by  = "terraform"
  }

  cleanup_policies {
    id     = "keep-latest-10"
    action = "KEEP"

    most_recent_versions {
      keep_count = 10
    }
  }

  cleanup_policies {
    id     = "delete-old-images"
    action = "DELETE"

    condition {
      older_than = "2592000s" # 30 days
    }
  }
}
