import test from 'node:test'
import assert from 'node:assert/strict'
import { copyText } from '../src/clipboard.js'

function fallbackDocument(copyResult = true) {
  const events = []
  const activeElement = { focus: () => events.push('restore-focus') }
  const textarea = {
    style: {},
    setAttribute: () => {},
    focus: () => events.push('focus'),
    select: () => events.push('select'),
    setSelectionRange: () => events.push('set-range'),
    remove: () => events.push('remove'),
  }

  return {
    events,
    textarea,
    documentObject: {
      activeElement,
      body: { appendChild: () => events.push('append') },
      createElement: () => textarea,
      execCommand: command => {
        events.push(command)
        return copyResult
      },
    },
  }
}

test('uses Clipboard API in a secure context', async () => {
  let copiedText = ''
  await copyText('SW-CODE', {
    secureContext: true,
    navigatorObject: { clipboard: { writeText: async value => { copiedText = value } } },
  })

  assert.equal(copiedText, 'SW-CODE')
})

test('falls back to execCommand outside a secure context', async () => {
  const fallback = fallbackDocument()
  await copyText('SW-CODE', {
    secureContext: false,
    navigatorObject: {},
    documentObject: fallback.documentObject,
  })

  assert.equal(fallback.textarea.value, 'SW-CODE')
  assert.deepEqual(fallback.events, ['append', 'focus', 'select', 'set-range', 'copy', 'remove', 'restore-focus'])
})

test('falls back when Clipboard API permission is denied', async () => {
  const fallback = fallbackDocument()
  await copyText('SW-CODE', {
    secureContext: true,
    navigatorObject: { clipboard: { writeText: async () => { throw new Error('denied') } } },
    documentObject: fallback.documentObject,
  })

  assert.ok(fallback.events.includes('copy'))
})

test('reports failure when the fallback copy command is rejected', async () => {
  const fallback = fallbackDocument(false)
  await assert.rejects(
    copyText('SW-CODE', {
      secureContext: false,
      navigatorObject: {},
      documentObject: fallback.documentObject,
    }),
    /rejected/,
  )
})
