const nonce = new URLSearchParams(location.search).get('nonce');
const literal = '<script>中国😀</script>';
window.chrome.webview.addEventListener('message', event => {
  const reply = event.data;
  if (reply.id !== 'bridge-roundtrip' || !reply.success || reply.value !== literal) return;
  document.querySelector('#status').textContent = '页面 → WPF → 页面：往返成功';
  document.querySelector('#literal').textContent = reply.value;
  window.chrome.webview.postMessage({
    id: 'bridge-ack', operation: 'echo', parameters: { value: `received:${reply.value}`, nonce }
  });
});
window.chrome.webview.postMessage({
  id: 'bridge-roundtrip', operation: 'echo', parameters: { value: literal, nonce }
});
