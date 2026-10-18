#!/usr/bin/env sh
# Собирает preview.html из RiftMockup.jsx (React + Babel из CDN, без сборщика).
cd "$(dirname "$0")"
{
cat <<'EOF'
<!doctype html>
<html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Rift mockup</title>
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@tabler/icons-webfont@3.19.0/dist/tabler-icons.min.css">
<style>html,body{margin:0}</style>
<script src="https://unpkg.com/react@18.3.1/umd/react.development.js"></script>
<script src="https://unpkg.com/react-dom@18.3.1/umd/react-dom.development.js"></script>
<script src="https://unpkg.com/@babel/standalone@7.24.7/babel.min.js"></script>
</head><body><div id="root"></div>
<script type="text/babel">
EOF
sed 's/^export default function/function/' RiftMockup.jsx
cat <<'EOF'
ReactDOM.createRoot(document.getElementById('root')).render(<RiftMockup />);
</script></body></html>
EOF
} > preview.html
