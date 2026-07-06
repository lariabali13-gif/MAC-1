const statusEl = document.getElementById('status');
const resultEl = document.getElementById('result');
const checkBtn = document.getElementById('checkBtn');
const sendTestBtn = document.getElementById('sendTestBtn');

// Check connection on load
checkConnection();

checkBtn.addEventListener('click', checkConnection);
sendTestBtn.addEventListener('click', sendTest);

async function checkConnection() {
  statusEl.textContent = 'Checking connection...';
  statusEl.className = 'status unknown';
  resultEl.style.display = 'none';

  try {
    const resp = await chrome.runtime.sendMessage({ type: 'PING_SERVICE' });
    if (resp && resp.status === 'ok') {
      statusEl.textContent = 'Connected to MAC-1 Service';
      statusEl.className = 'status connected';
      showResult('Service Response:', JSON.stringify(resp, null, 2));
    } else {
      statusEl.textContent = 'Service responded with error';
      statusEl.className = 'status disconnected';
      showResult('Response:', JSON.stringify(resp, null, 2));
    }
  } catch (e) {
    statusEl.textContent = 'Service not reachable';
    statusEl.className = 'status disconnected';
    showResult('Error:', e.message);
  }
}

async function sendTest() {
  statusEl.textContent = 'Sending test data...';
  statusEl.className = 'status unknown';
  
  try {
    const result = await chrome.runtime.sendMessage({ type: 'SEND_TEST' });
    if (result && result.success) {
      statusEl.textContent = 'Test data sent successfully!';
      statusEl.className = 'status connected';
      showResult('Server Response:', JSON.stringify(result, null, 2));
    } else {
      statusEl.textContent = 'Send failed';
      statusEl.className = 'status disconnected';
      showResult('Error:', JSON.stringify(result, null, 2));
    }
  } catch (e) {
    statusEl.textContent = 'Send failed';
    statusEl.className = 'status disconnected';
    showResult('Error:', e.message);
  }
}

function showResult(label, text) {
  resultEl.style.display = 'block';
  resultEl.textContent = label + '\n' + text;
}
