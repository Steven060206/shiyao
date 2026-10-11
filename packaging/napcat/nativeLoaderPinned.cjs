const path = require('path');
const { pathToFileURL } = require('url');

async function initializeNapCat(session, loginService, registerCallback) {
  console.log('[MessagePurify] Starting pinned group loader.');
  try {
    const runtime = await import(pathToFileURL(path.join(__dirname, 'messagepurify-napcat.mjs')).href);
    const { registerPinnedGroups } = await import(pathToFileURL(path.join(__dirname, 'pinned-groups.mjs')).href);
    await runtime.NCoreInitFramework(session, loginService, callback => registerCallback(callback));
    registerPinnedGroups(runtime.WebUiDataRuntime.getOneBotContext(), runtime.OneBotAction);
    console.log('[MessagePurify] Pinned group read API ready.');
  } catch (error) {
    console.log('[MessagePurify] Pinned group read API unavailable. Error type:', error?.name, 'Reason:', error?.message);
  }
}

module.exports = { initializeNapCat };
