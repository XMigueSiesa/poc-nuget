# ---------------------------------------------------------------------------
# monitoring.tf — Uptime checks and alert policies for CloudHub
# ---------------------------------------------------------------------------

# --- Notification channel (email) -----------------------------------------

resource "google_monitoring_notification_channel" "email_alerts" {
  display_name = "SIESA POS Alert Email"
  type         = "email"

  labels = {
    email_address = var.alert_email
  }

  # Skip creation when no email is configured (keeps staging environments clean)
  count = var.alert_email != "" ? 1 : 0
}

locals {
  notification_channels = var.alert_email != "" ? [google_monitoring_notification_channel.email_alerts[0].id] : []
}

# --- Uptime check on /health/ready ----------------------------------------

resource "google_monitoring_uptime_check_config" "cloudhub_ready" {
  display_name = "pos-cloudhub-readiness"
  timeout      = "10s"
  period       = "60s"

  http_check {
    path         = "/health/ready"
    port         = 443
    use_ssl      = true
    validate_ssl = true

    accepted_response_status_codes {
      status_class = "STATUS_CLASS_2XX"
    }
  }

  monitored_resource {
    type = "uptime_url"
    labels = {
      project_id = var.project_id
      host       = trimprefix(google_cloud_run_v2_service.cloudhub.uri, "https://")
    }
  }

  depends_on = [google_cloud_run_v2_service.cloudhub]
}

# --- Alert: HTTP 5xx error rate > 5% --------------------------------------

resource "google_monitoring_alert_policy" "high_error_rate" {
  display_name = "pos-cloudhub: Error Rate > 5%"
  combiner     = "OR"

  conditions {
    display_name = "HTTP 5xx responses exceed 5%"

    condition_monitoring_query_language {
      query = <<-MQL
        fetch cloud_run_revision
        | metric 'run.googleapis.com/request_count'
        | filter resource.service_name == 'pos-cloudhub'
        | align rate(1m)
        | group_by [metric.response_code_class]
        | {
            t_0: filter metric.response_code_class == '5xx'
            ; t_1: ident
          }
        | ratio
        | every 1m
        | condition val() > 0.05 '1'
      MQL

      duration = "300s"

      trigger {
        count = 1
      }
    }
  }

  notification_channels = local.notification_channels

  alert_strategy {
    auto_close = "1800s"
  }

  documentation {
    content   = "CloudHub is returning more than 5% HTTP 5xx responses. Investigate application logs in Cloud Logging and check Cloud SQL connectivity."
    mime_type = "text/markdown"
  }
}

# --- Alert: instance count = 0 (service down) -----------------------------

resource "google_monitoring_alert_policy" "service_down" {
  display_name = "pos-cloudhub: No Running Instances"
  combiner     = "OR"

  conditions {
    display_name = "Active instance count dropped to zero"

    condition_monitoring_query_language {
      query = <<-MQL
        fetch cloud_run_revision
        | metric 'run.googleapis.com/container/instance_count'
        | filter resource.service_name == 'pos-cloudhub'
        | filter metric.state == 'active'
        | align mean(1m)
        | group_by []
        | condition val() < 1
      MQL

      duration = "120s"

      trigger {
        count = 1
      }
    }
  }

  notification_channels = local.notification_channels

  alert_strategy {
    auto_close = "1800s"
  }

  documentation {
    content   = "CloudHub has zero active instances. The service may have crashed or been accidentally deleted. Check the Cloud Run console and recent deployment history."
    mime_type = "text/markdown"
  }
}
