// The Framework loader supplies native QQ session data, never chat bodies.
export const actionName = 'messagepurify_get_pinned_groups';

export function extractPinnedGroups(snapshot, accountId) {
  const info = snapshot?.info;
  if (snapshot?.result !== 0 || info?.errCode !== 0 || !Array.isArray(info.changedList)
      || !Array.isArray(info.sortedContactList) || info.sortedContactList.length !== info.changedList.length
      || info.changedList.length >= 10000) throw new Error('pinned-state-unavailable');
  const groups = new Set();
  for (const contact of info.changedList) {
    if (contact.chatType !== 2) continue;
    // Missing/unknown flags are an error, never an implicit "not pinned".
    if (contact.topFlag !== 0 && contact.topFlag !== 1) throw new Error('pinned-state-unavailable');
    if (contact.topFlag === 1) {
      if (typeof contact.peerUin !== 'string' || !/^[1-9][0-9]*$/.test(contact.peerUin))
        throw new Error('pinned-state-unavailable');
      groups.add(contact.peerUin);
    }
  }
  return { self_id: accountId, group_ids: [...groups].map(group_id => ({ group_id })) };
}

export function registerPinnedGroups(context, OneBotAction) {
  if (typeof context?.actions?.get !== 'function' || !context.core?.context?.session) throw new Error('pinned-state-unavailable');
  class GetPinnedGroups extends OneBotAction {
    actionName = actionName;
    async _handle(payload) {
      const snapshot = await this.core.context.session.getRecentContactService().getRecentContactListSnapShot(10000);
      if (payload?.schema_only === true) {
        const info = snapshot?.info;
        const contacts = Array.isArray(info?.changedList) ? info.changedList : [];
        // Diagnostic schema contains field names/types/counts only, no peer IDs or message data.
        return { schema: {
          rootFields: Object.keys(snapshot ?? {}), infoFields: Object.keys(info ?? {}),
          contactCount: contacts.length,
          groupCount: contacts.filter(contact => contact.chatType === 2).length,
          groupFields: [...new Set(contacts.filter(contact => contact.chatType === 2).flatMap(Object.keys))],
          flagTypes: [...new Set(contacts.filter(contact => contact.chatType === 2).flatMap(contact =>
            Object.entries(contact).filter(([key]) => /top|pin|stick/i.test(key)).map(([key, value]) => key + ':' + typeof value)))],
          topFlags: [...new Set(contacts.filter(contact => contact.chatType === 2).map(contact => contact.topFlag))]
            .map(value => ({ value, count: contacts.filter(contact => contact.chatType === 2 && contact.topFlag === value).length })),
          sortedCount: Array.isArray(info?.sortedContactList) ? info.sortedContactList.length : null,
          errCode: info?.errCode
        } };
      }
      return extractPinnedGroups(snapshot, this.core.selfInfo.uin);
    }
  }
  const handler = new GetPinnedGroups(context, context.core);
  // NapCat v4.18.37 exposes {get, call}, not the underlying Map.
  const originalGet = context.actions.get.bind(context.actions);
  context.actions.get = key => key === actionName ? handler : originalGet(key);
}
