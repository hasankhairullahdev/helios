{{/*
Expand the name of the chart.
*/}}
{{- define "notification-service.name" -}}
{{- .Chart.Name }}
{{- end }}

{{/*
Full name: release-name + chart-name (dipotong max 63 karakter — limit DNS label)
*/}}
{{- define "notification-service.fullname" -}}
{{- printf "%s-%s" .Release.Name .Chart.Name | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Standard labels — dipasang di semua resource supaya konsisten dan bisa di-select
*/}}
{{- define "notification-service.labels" -}}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
app.kubernetes.io/name: {{ include "notification-service.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Values.image.tag | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels — dipakai oleh Service dan Deployment untuk match pods
Lebih minimal dari labels supaya selector tidak berubah saat upgrade
*/}}
{{- define "notification-service.selectorLabels" -}}
app.kubernetes.io/name: {{ include "notification-service.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}
