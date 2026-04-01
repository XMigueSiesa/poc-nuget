# ---------------------------------------------------------------------------
# cloud-sql.tf — PostgreSQL 17 instance, database, and app user
# ---------------------------------------------------------------------------

resource "random_password" "pos_app_password" {
  length  = 32
  special = false
}

resource "google_sql_database_instance" "pos_db" {
  name             = "pos-db-${var.environment}"
  database_version = "POSTGRES_17"
  region           = var.region

  deletion_protection = true

  depends_on = [google_service_networking_connection.private_vpc_connection]

  settings {
    tier              = var.db_tier
    availability_type = "REGIONAL"
    edition           = "ENTERPRISE"

    ip_configuration {
      ipv4_enabled                                  = false
      private_network                               = google_compute_network.pos_vpc.id
      enable_private_path_for_google_cloud_services = true
    }

    disk_autoresize       = true
    disk_autoresize_limit = 100
    disk_size             = 10
    disk_type             = "PD_SSD"

    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = true
      start_time                     = "04:00"
      transaction_log_retention_days = 7

      backup_retention_settings {
        retained_backups = 14
        retention_unit   = "COUNT"
      }
    }

    maintenance_window {
      day          = 7 # Sunday
      hour         = 4 # 04:00 UTC
      update_track = "stable"
    }

    database_flags {
      name  = "log_min_duration_statement"
      value = "1000"
    }

    database_flags {
      name  = "log_connections"
      value = "on"
    }

    database_flags {
      name  = "log_disconnections"
      value = "on"
    }

    insights_config {
      query_insights_enabled  = true
      query_string_length     = 1024
      record_application_tags = true
      record_client_address   = false
    }
  }
}

resource "google_sql_database" "pos_cloud" {
  name     = "pos_cloud"
  instance = google_sql_database_instance.pos_db.name
}

resource "google_sql_user" "pos_app" {
  name     = "pos_app"
  instance = google_sql_database_instance.pos_db.name
  password = random_password.pos_app_password.result

  lifecycle {
    ignore_changes = [password]
  }
}
