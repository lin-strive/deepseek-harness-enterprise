export async function copyText(text, environment = {}) {
  const navigatorObject = environment.navigatorObject ?? globalThis.navigator
  const documentObject = environment.documentObject ?? globalThis.document
  const secureContext = environment.secureContext ?? globalThis.isSecureContext

  if (secureContext && navigatorObject?.clipboard?.writeText) {
    try {
      await navigatorObject.clipboard.writeText(text)
      return
    } catch {
      // Some browsers expose the API but deny it through permissions policy.
      // Fall back to the selection-based copy path while the click is active.
    }
  }

  if (!documentObject?.body || typeof documentObject.execCommand !== 'function') {
    throw new Error('Clipboard access is unavailable')
  }

  const activeElement = documentObject.activeElement
  const textarea = documentObject.createElement('textarea')
  textarea.value = text
  textarea.setAttribute('readonly', '')
  textarea.setAttribute('aria-hidden', 'true')
  textarea.style.position = 'fixed'
  textarea.style.inset = '0 auto auto -9999px'
  textarea.style.opacity = '0'
  documentObject.body.appendChild(textarea)

  let copied = false
  try {
    textarea.focus({ preventScroll: true })
    textarea.select()
    textarea.setSelectionRange(0, textarea.value.length)
    copied = documentObject.execCommand('copy')
  } finally {
    textarea.remove()
    activeElement?.focus?.({ preventScroll: true })
  }

  if (!copied) throw new Error('Clipboard copy command was rejected')
}
