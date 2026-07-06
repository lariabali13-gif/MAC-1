// MAC-1 Extension v0.2.0 - Real Download Capture
const SERVICE_URL = 'http://127.0.0.1:57575';

class Mac1Extension {
  constructor() {
    this.settings = { enabled: true, port: 57575 };
    // Store captured headers per URL
    this.capturedHeaders = new Map();
    this.capturedResponseHeaders = new Map();
    this.capturedPostData = new Map();
  }

  async init() {
    await this.loadSettings();
    this.setupListeners();
    await this.checkConnection();
    this.log('Extension initialized v0.2.0');
  }

  log(...args) {
    console.log('[MAC-1]', ...args);
  }

  async loadSettings() {
    try {
      const stored = await chrome.storage.local.get('settings');
      if (stored.settings) this.settings = { ...this.settings, ...stored.settings };
    } catch (e) {}
  }

  setupListeners() {
    // === Capture ALL request headers ===
    chrome.webRequest.onBeforeSendHeaders.addListener(
      (details) => this.captureRequestHeaders(details),
      { urls: ['<all_urls>'] },
      ['requestHeaders', 'extraHeaders']
    );

    // === Capture ALL response headers ===
    chrome.webRequest.onHeadersReceived.addListener(
      (details) => this.captureResponseHeaders(details),
      { urls: ['<all_urls>'] },
      ['responseHeaders', 'extraHeaders']
    );

    // === Capture POST data ===
    chrome.webRequest.onBeforeRequest.addListener(
      (details) => this.capturePostData(details),
      { urls: ['<all_urls>'] },
      ['requestBody']
    );

    // === Track redirects ===
    this.pendingRedirects = new Map();
    chrome.webRequest.onBeforeRedirect.addListener(
      (details) => {
        if (details.redirectUrl && details.statusCode >= 300 && details.statusCode < 400) {
          this.pendingRedirects.set(details.url, details.redirectUrl);
          setTimeout(() => this.pendingRedirects.delete(details.url), 30000);
        }
      },
      { urls: ['<all_urls>'] }
    );

    // === MAIN: Download created - CANCEL IMMEDIATELY ===
    chrome.downloads.onCreated.addListener((item) => this.onDownloadCreated(item));

    // === Messages from popup ===
    chrome.runtime.onMessage.addListener((msg, sender, respond) => {
      if (msg.type === 'CHECK_CONNECTION') {
        this.checkConnection().then(ok => respond({ connected: ok }));
        return true;
      }
      if (msg.type === 'PING_SERVICE') {
        this.pingService().then(result => respond(result));
        return true;
      }
      if (msg.type === 'GET_STATUS') {
        respond({ enabled: this.settings.enabled });
        return true;
      }
    });
  }

  // === Capture request headers ===
  captureRequestHeaders(details) {
    if (!details.requestHeaders) return;
    const headers = [];
    for (const h of details.requestHeaders) {
      headers.push({ name: h.name, value: h.value });
    }
    this.capturedHeaders.set(details.url, {
      headers,
      method: details.method,
      tabId: details.tabId,
      timestamp: Date.now()
    });
    // Also store by tabId for fallback
    if (details.tabId >= 0) {
      this.capturedHeaders.set('tab:' + details.tabId, {
        url: details.url,
        headers,
        method: details.method,
        timestamp: Date.now()
      });
    }
  }

  // === Capture response headers ===
  captureResponseHeaders(details) {
    if (!details.responseHeaders) return;
    const headers = [];
    for (const h of details.responseHeaders) {
      headers.push({ name: h.name, value: h.value });
    }
    this.capturedResponseHeaders.set(details.url, {
      headers,
      statusCode: details.statusCode,
      timestamp: Date.now()
    });
    if (details.tabId >= 0) {
      this.capturedResponseHeaders.set('tab:' + details.tabId, {
        url: details.url,
        headers,
        statusCode: details.statusCode,
        timestamp: Date.now()
      });
    }
  }

  // === Capture POST data ===
  capturePostData(details) {
    if (!details.requestBody) return;
    let data = null;
    if (details.requestBody.formData) {
      data = details.requestBody.formData;
    } else if (details.requestBody.raw) {
      try {
        const decoder = new TextDecoder('utf-8');
        data = decoder.decode(details.requestBody.raw[0].bytes);
      } catch (e) {}
    }
    if (data) {
      this.capturedPostData.set(details.url, { data, timestamp: Date.now() });
      this.capturedPostData.set('tab:' + details.tabId, { url: details.url, data, timestamp: Date.now() });
    }
  }

  // === MAIN: Download created handler ===
  async onDownloadCreated(item) {
    if (!this.settings.enabled) return;
    if (this.shouldSkipUrl(item.url)) return;

    this.log('Download detected:', item.url);

    // === CANCEL IMMEDIATELY - no flash to user ===
    try {
      await chrome.downloads.cancel(item.id);
      this.log('Download cancelled:', item.url);
    } catch (e) {
      this.log('Cancel failed (may already be cancelled):', e.message);
    }

    // === Wait briefly for headers to be captured ===
    await new Promise(r => setTimeout(r, 200));

    // === Collect all data ===
    const sessionData = await this.collectSessionData(item);

    // === Log what we captured ===
    this.logCaptureSummary(sessionData);

    // === Send to service ===
    await this.sendToService(sessionData);
  }

  // === Collect complete session data ===
  async collectSessionData(item) {
    const url = item.url;
    const tabId = item.tabId;

    // Get request headers
    let requestHeaders = this.capturedHeaders.get(url);
    if (!requestHeaders && tabId >= 0) {
      requestHeaders = this.capturedHeaders.get('tab:' + tabId);
    }
    // Fallback: use most recent headers
    if (!requestHeaders) {
      const lastEntry = this.capturedHeaders.values().next().value;
      if (lastEntry && !lastEntry.url?.startsWith('tab:')) requestHeaders = lastEntry;
    }

    // Get response headers
    let responseHeaders = this.capturedResponseHeaders.get(url);
    if (!responseHeaders && tabId >= 0) {
      responseHeaders = this.capturedResponseHeaders.get('tab:' + tabId);
    }

    // Get POST data
    let postData = this.capturedPostData.get(url);
    if (!postData && tabId >= 0) {
      postData = this.capturedPostData.get('tab:' + tabId);
    }

    // Get cookies
    let cookies = [];
    try {
      const allCookies = await chrome.cookies.getAll({ url });
      cookies = allCookies.map(c => ({
        name: c.name,
        value: c.value,
        domain: c.domain,
        path: c.path,
        secure: c.secure,
        httpOnly: c.httpOnly,
        sameSite: c.sameSite,
        expires: c.expirationDate || null
      }));
    } catch (e) {}

    // Get tab info
    let tabInfo = null;
    try {
      if (tabId >= 0) {
        const tab = await chrome.tabs.get(tabId);
        tabInfo = { url: tab.url, title: tab.title };
      }
    } catch (e) {}

    // Resolve redirect
    const finalUrl = this.pendingRedirects.get(url) || item.finalUrl || url;

    // Build headers as key-value for easy logging
    const headersDict = {};
    if (requestHeaders?.headers) {
      for (const h of requestHeaders.headers) {
        headersDict[h.name.toLowerCase()] = h.value;
      }
    }

    // Get filename from multiple sources
    let filename = this.extractFilename(url);
    
    // Build response headers dict
    const responseHeadersDict = {};
    if (responseHeaders?.headers) {
      for (const h of responseHeaders.headers) {
        responseHeadersDict[h.name.toLowerCase()] = h.value;
      }
    }
    
    // Try Content-Disposition from RESPONSE headers first (most reliable)
    const cdHeader = responseHeadersDict['content-disposition'];
    if (cdHeader) {
      const match = cdHeader.match(/filename\*?=(?:UTF-8''|"?)([^";\s]+)/i);
      if (match) {
        try { filename = decodeURIComponent(match[1].replace(/"/g, '')); } 
        catch { filename = match[1].replace(/"/g, ''); }
      }
    }
    
    // Try Chrome's download item filename
    if (filename === 'download' && item.filename) {
      const parts = item.filename.split(/[/\\]/);
      filename = parts[parts.length - 1] || filename;
    }

    // Determine method - CDN cross-domain redirects always use GET
    let method = requestHeaders?.method || 'GET';
    if (finalUrl && url && finalUrl !== url) {
      try {
        const origHost = new URL(url).hostname;
        const finalHost = new URL(finalUrl).hostname;
        if (origHost !== finalHost) {
          method = 'GET';
          this.log('Cross-domain redirect detected, forcing GET method');
        }
      } catch (e) {}
    }

    return {
      url: url,
      finalUrl: finalUrl,
      filename: filename,
      fileSize: item.fileSize || 0,
      mimeType: item.mime || '',
      method: method,
      referrer: headersDict['referer'] || headersDict['referrer'] || tabInfo?.url || '',
      userAgent: headersDict['user-agent'] || '',
      headers: headersDict,
      rawHeaders: requestHeaders?.headers || [],
      responseHeaders: responseHeaders?.headers || [],
      cookies: cookies,
      postData: postData?.data || null,
      tab: tabInfo,
      timestamp: Date.now()
    };
  }

  // === Log capture summary ===
  logCaptureSummary(session) {
    const headerCount = Object.keys(session.headers).length;
    const cookieCount = session.cookies.length;
    const rawHeaderCount = session.rawHeaders.length;
    const responseHeaderCount = session.responseHeaders.length;

    // Check for important headers
    const hasSecFetchDest = !!session.headers['sec-fetch-dest'];
    const hasSecFetchMode = !!session.headers['sec-fetch-mode'];
    const hasSecFetchSite = !!session.headers['sec-fetch-site'];
    const hasSecFetchUser = !!session.headers['sec-fetch-user'];
    const hasSecChUa = !!session.headers['sec-ch-ua'];
    const hasSecChUaMobile = !!session.headers['sec-ch-ua-mobile'];
    const hasSecChUaPlatform = !!session.headers['sec-ch-ua-platform'];
    const hasUserAgent = !!session.headers['user-agent'];
    const hasReferer = !!session.headers['referer'] || !!session.headers['referrer'];
    const hasCookie = !!session.headers['cookie'];
    const hasOrigin = !!session.headers['origin'];
    const hasAcceptEncoding = !!session.headers['accept-encoding'];
    const hasUpgradeInsecure = !!session.headers['upgrade-insecure-requests'];
    const hasConnection = !!session.headers['connection'];

    console.log('');
    console.log('╔══════════════════════════════════════════════╗');
    console.log('║     MAC-1 Download Capture Report           ║');
    console.log('╚══════════════════════════════════════════════╝');
    console.log('');
    console.log(`URL: ${session.url}`);
    console.log(`Final URL: ${session.finalUrl}`);
    console.log(`Filename: ${session.filename}`);
    console.log(`File Size: ${session.fileSize} bytes (${this.formatSize(session.fileSize)})`);
    console.log(`MIME Type: ${session.mimeType || 'unknown'}`);
    console.log(`Method: ${session.method}`);
    console.log('');
    console.log('--- CAPTURE SUMMARY ---');
    console.log(`Request Headers: ${rawHeaderCount} captured`);
    console.log(`Response Headers: ${responseHeaderCount} captured`);
    console.log(`Cookies: ${cookieCount} captured`);
    console.log(`Post Data: ${session.postData ? 'YES' : 'NO'}`);
    console.log('');
    console.log('--- KEY HEADERS CHECK ---');
    console.log(`User-Agent:       ${hasUserAgent ? '✅' : '❌'} ${session.headers['user-agent']?.substring(0, 60) || 'missing'}`);
    console.log(`Referer:          ${hasReferer ? '✅' : '❌'} ${session.headers['referer']?.substring(0, 60) || 'missing'}`);
    console.log(`Origin:           ${hasOrigin ? '✅' : '❌'} ${session.headers['origin'] || 'missing'}`);
    console.log(`Cookie:           ${hasCookie ? '✅' : '❌'}`);
    console.log(`Accept-Encoding:  ${hasAcceptEncoding ? '✅' : '❌'} ${session.headers['accept-encoding'] || 'missing'}`);
    const hasZstd = session.headers['accept-encoding']?.includes('zstd') || false;
    console.log(`  └─ zstd:         ${hasZstd ? '✅' : '❌'}`);
    console.log(`Sec-Fetch-Dest:   ${hasSecFetchDest ? '✅' : '❌'} ${session.headers['sec-fetch-dest'] || 'missing'}`);
    console.log(`Sec-Fetch-Mode:   ${hasSecFetchMode ? '✅' : '❌'} ${session.headers['sec-fetch-mode'] || 'missing'}`);
    console.log(`Sec-Fetch-Site:   ${hasSecFetchSite ? '✅' : '❌'} ${session.headers['sec-fetch-site'] || 'missing'}`);
    console.log(`Sec-Fetch-User:   ${hasSecFetchUser ? '✅' : '❌'} ${session.headers['sec-fetch-user'] || 'missing'}`);
    console.log(`Sec-CH-UA:        ${hasSecChUa ? '✅' : '❌'} ${session.headers['sec-ch-ua']?.substring(0, 50) || 'missing'}`);
    console.log(`Sec-CH-UA-Mobile: ${hasSecChUaMobile ? '✅' : '❌'} ${session.headers['sec-ch-ua-mobile'] || 'missing'}`);
    console.log(`Sec-CH-UA-Platform:${hasSecChUaPlatform ? '✅' : '❌'} ${session.headers['sec-ch-ua-platform'] || 'missing'}`);
    console.log(`Upgrade-Insecure: ${hasUpgradeInsecure ? '✅' : '❌'} ${session.headers['upgrade-insecure-requests'] || 'missing'}`);
    console.log(`Connection:       ${hasConnection ? '✅' : '❌'} ${session.headers['connection'] || 'missing'}`);
    console.log('');
    console.log('--- ALL HEADERS ---');
    for (const h of session.rawHeaders) {
      console.log(`  ${h.name}: ${h.value.substring(0, 100)}`);
    }
    console.log('');
    console.log('--- ALL COOKIES ---');
    for (const c of session.cookies) {
      console.log(`  ${c.name}=${c.value.substring(0, 30)}... (domain=${c.domain}, path=${c.path}, secure=${c.secure})`);
    }
    console.log('');
    console.log('══════════════════════════════════════════════');
    console.log(`Total data captured: ${rawHeaderCount} headers, ${cookieCount} cookies`);
    
    // Calculate capture quality score
    let score = 0;
    if (hasUserAgent) score += 10;
    if (hasReferer) score += 10;
    if (hasCookie) score += 10;
    if (hasAcceptEncoding) score += 10;
    if (hasSecFetchDest) score += 5;
    if (hasSecFetchMode) score += 5;
    if (hasSecFetchSite) score += 5;
    if (hasSecFetchUser) score += 5;
    if (hasSecChUa) score += 5;
    if (hasSecChUaMobile) score += 5;
    if (hasSecChUaPlatform) score += 5;
    if (hasUpgradeInsecure) score += 5;
    if (hasConnection) score += 5;
    if (hasOrigin) score += 5;
    if (hasZstd) score += 5;
    
    console.log(`Capture Quality: ${score}/100`);
    console.log('══════════════════════════════════════════════');
  }

  // === Send to service ===
  async sendToService(sessionData) {
    if (!this.communicatorConnected) {
      const connected = await this.checkConnection();
      if (!connected) {
        this.log('Service not connected, queuing data');
        return false;
      }
    }

    try {
      const resp = await fetch(`${SERVICE_URL}/api/session`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(sessionData),
        signal: AbortSignal.timeout(5000)
      });
      const result = await resp.json();
      if (result.success) {
        this.log(`Session sent to service ✅ sessionId=${result.sessionId}`);
        return true;
      } else {
        this.log('Service rejected session:', result.error);
        return false;
      }
    } catch (e) {
      this.log('Failed to send to service:', e.message);
      return false;
    }
  }

  // === Helpers ===
  shouldSkipUrl(url) {
    return !url || url.startsWith('chrome-extension://') || url.startsWith('chrome://') ||
           url.startsWith('about:') || url.startsWith('data:') || url.startsWith('blob:');
  }

  extractFilename(url) {
    try {
      const urlObj = new URL(url);
      // Try pathname first
      const parts = urlObj.pathname.split('/');
      const name = parts[parts.length - 1];
      if (name && name.includes('.')) return decodeURIComponent(name);
      
      // Try to find filename in query params
      for (const [key, value] of urlObj.searchParams) {
        if (value && value.includes('.') && value.length < 100) {
          const valParts = value.split('/');
          const valName = valParts[valParts.length - 1];
          if (valName && valName.includes('.')) return decodeURIComponent(valName);
        }
      }
    } catch (e) {}
    return 'download';
  }

  formatSize(bytes) {
    if (bytes <= 0) return '0 B';
    const sizes = ['B', 'KB', 'MB', 'GB'];
    let i = 0, size = bytes;
    while (size >= 1024 && i < sizes.length - 1) { size /= 1024; i++; }
    return `${size.toFixed(1)} ${sizes[i]}`;
  }

  async checkConnection() {
    try {
      const resp = await fetch(`${SERVICE_URL}/api/health`, {
        method: 'GET',
        signal: AbortSignal.timeout(3000)
      });
      if (resp.ok) {
        this.communicatorConnected = true;
        return true;
      }
    } catch (e) {}
    this.communicatorConnected = false;
    return false;
  }

  async pingService() {
    try {
      const resp = await fetch(`${SERVICE_URL}/api/health`, {
        method: 'GET',
        signal: AbortSignal.timeout(3000)
      });
      return await resp.json();
    } catch (e) {
      return { error: e.message };
    }
  }
}

// Initialize
const mac1 = new Mac1Extension();
mac1.init();
