/* All article content is inserted as text, never as HTML. */
(() => {
  const $ = id => document.getElementById(id);
  let api, polling = false, lastNotes = '', settings = {}, pushed = 0;
  const value = n => n === null || n === undefined ? '—' : Number(n).toLocaleString('zh-CN');
  function renderNotes(notes) {
    const fingerprint = JSON.stringify(notes);
    if (fingerprint === lastNotes) return;
    lastNotes = fingerprint;
    $('notes').replaceChildren();
    notes.forEach(note => {
      const article = document.createElement('article'); article.className = 'note';
      const title = document.createElement('h2'); title.className = 'note-title';
      title.textContent = note.title; title.title = note.title;
      const metrics = document.createElement('div'); metrics.className = 'metrics';
      [[note.read_label, note.read_count], ['评论', note.comment_count], ['收藏', note.collect_count]].forEach(([label, n]) => {
        const metric = document.createElement('div'); metric.className = 'metric';
        const caption = document.createElement('span'); caption.className = 'metric-label'; caption.textContent = label;
        const number = document.createElement('strong'); number.className = 'metric-value'; number.textContent = value(n); number.title = value(n);
        metric.append(caption, number); metrics.append(metric);
      });
      article.append(title, metrics); $('notes').append(article);
    });
  }
  function render(state) {
    document.documentElement.dataset.theme = settings.theme === 'transparent' ? 'transparent' : 'glass';
    renderNotes(state.notes);
    const populated = state.notes.length > 0;
    $('notes').hidden = !populated; $('empty').hidden = populated;
    $('count').textContent = populated ? `${state.notes.length} 篇 · 按发布时间` : '最近 5 篇';
    $('status').textContent = state.message; $('status').title = state.message;
    $('dot').className = `status-dot ${state.status}`;
    const blocked = ['login_required', 'verification_required'].includes(state.status);
    $('refresh').disabled = state.status === 'syncing' || blocked;
    $('login').textContent = blocked ? '继续登录' : '打开后台';
    $('time').textContent = state.last_success ? `${state.cached ? '上次数据 · ' : '更新于 '}${new Date(state.last_success).toLocaleString('zh-CN', {month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',hour12:false})}` : '尚未同步';
    $('hint').textContent = settings.click_through ? '穿透开启 · 托盘关闭' : blocked ? '自动同步已暂停' : '每 15 分钟更新';
    $('pin').setAttribute('aria-pressed', String(!!settings.on_top));
    if (!populated) {
      $('empty-text').textContent = state.status === 'ready' ? '账号暂时没有已发布的文章。' : state.status === 'error' ? state.message : '登录小红书创作后台后，最近文章的数据会显示在这里。';
      $('empty-login').textContent = state.status === 'ready' ? '打开创作后台' : '登录创作后台';
    }
  }
  async function poll() {
    if (!api || polling) return;
    polling = true;
    const before = pushed;
    try { const result = await api.snapshot(); if (before === pushed) { settings = result.settings; render(result.data); } }
    catch { $('status').textContent = '连接桌面服务失败'; $('dot').className = 'status-dot error'; }
    finally { polling = false; }
  }
  window.xhsBoardRender = result => { pushed++; settings = result.settings; render(result.data); };
  async function act(action) {
    try { await action(); await poll(); }
    catch (e) { $('status').textContent = '操作未完成，请重试'; $('status').title = String(e); }
  }
  window.addEventListener('pywebviewready', () => {
    api = window.pywebview.api;
    $('refresh').onclick = () => act(() => api.refresh());
    $('login').onclick = $('empty-login').onclick = () => act(() => api.login());
    $('pin').onclick = () => act(() => api.pin(!settings.on_top));
    $('hide').onclick = () => act(() => api.hide());
    $('settings').onclick = async () => {
      const options = await api.options();
      $('opt-theme').value = options.theme || 'glass';
      $('opt-pin').checked = options.on_top; $('opt-pass').checked = options.click_through; $('opt-start').checked = options.autostart;
      $('options').showModal();
    };
    $('close-options').onclick = () => $('options').close();
    $('opt-theme').onchange = () => act(async () => {
      try { await api.set_theme($('opt-theme').value); }
      catch(e) { $('opt-theme').value = settings.theme || 'glass'; throw e; }
    });
    [['opt-pin','on_top'],['opt-pass','click_through'],['opt-start','autostart']].forEach(([id, key]) => {
      $(id).onchange = () => act(async () => {
        try { await api.set_option(key, $(id).checked); if (key === 'click_through' && $(id).checked) $('options').close(); }
        catch(e) { $(id).checked = !$(id).checked; throw e; }
      });
    });
    poll();
  });
})();
