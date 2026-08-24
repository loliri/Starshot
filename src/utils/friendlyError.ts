import { t, type I18nKey } from '../i18n';

export const friendlyError = (
  error: string | { message: string } | unknown,
): string => {
  const errStr =
    typeof error === 'string'
      ? error
      : error && typeof error === 'object' && 'message' in error
        ? (error as { message: string }).message
        : JSON.stringify(error);
  // 空格，换行符，制表符，右括号，逗号都是url结束
  const firstUrlInstr = errStr.match(/https?:\/\/[^\s),]+/);
  // 替换url时保留url结束标志字符，避免把右括号等也替换掉
  const errStrWithoutUrl = errStr.replace(/https?:\/\/[^\s),]+/g, '[url]');
  let friendlyKey: I18nKey | null = null;
  const checkStr = errStrWithoutUrl.toLowerCase();
  if (errStr.includes('operation timed out')) {
    friendlyKey = 'net.timeout';
  } else if (checkStr.includes('connection refused')) {
    friendlyKey = 'net.refused';
  } else if (checkStr.includes('connection reset')) {
    friendlyKey = 'net.reset';
  } else if (checkStr.includes('too_slow') || checkStr.includes('stalled')) {
    friendlyKey = 'net.slow';
  }

  if (friendlyKey) {
    return `${t(friendlyKey)}\n\n${t('net.originalError')}${errStrWithoutUrl}${firstUrlInstr ? `\n\n${t('net.server')}${firstUrlInstr[0]}` : ''}`;
  } else {
    return `${errStrWithoutUrl}${firstUrlInstr ? `\n\n${t('net.server')}${firstUrlInstr[0]}` : ''}`;
  }
};
