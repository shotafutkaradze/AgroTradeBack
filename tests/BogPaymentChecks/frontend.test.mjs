import test from 'node:test'
import assert from 'node:assert/strict'
import { calculatorOptions, loanSelection, paymentResultFromStatus, loadBogSdk } from '../../../agrotrade-front/src/services/bog.js'

test('independent buttons select the exact BOG method and trusted total', () => {
  assert.equal(calculatorOptions('bog-installment', 123.45, {}).bnpl, false)
  assert.equal(calculatorOptions('bog-parts', 123.45, {}).bnpl, true)
  assert.equal(calculatorOptions('bog-parts', 123.45, {}).amount, 123.45)
  assert.throws(() => calculatorOptions('card', 10, {}))
})
test('calculator selection preserves the bank code and never sends an amount', () => {
  assert.deepEqual(loanSelection({ month: 4, discount_code: 'CampaignABC', amount: 1 }, 'capability'), {
    installmentMonth: 4, installmentType: 'CampaignABC', paymentAccessToken: 'capability'
  })
  assert.throws(() => loanSelection({ month: 0, discount_code: '' }, 'capability'))
})
test('only confirmed payment status produces a success screen', () => {
  for (const status of ['created', 'processing', 'pending', 'blocked', 'unknown', undefined]) {
    assert.equal(paymentResultFromStatus(status), 'pending')
  }
  assert.equal(paymentResultFromStatus('paid'), 'success')
  assert.equal(paymentResultFromStatus('failed'), 'error')
})
test('SDK errors allow a retry and do not cache a rejected promise', async () => {
  const browser = {}
  let attempt = 0
  const doc = {
    createElement: () => ({ remove() {} }),
    head: { appendChild(script) {
      attempt++
      queueMicrotask(() => {
        if (attempt === 1) script.onerror()
        else { browser.BOG = { Calculator: { open() {} } }; script.onload() }
      })
    } }
  }
  await assert.rejects(loadBogSdk('client', browser, doc))
  assert.equal(browser.__bogSdkPromise, null)
  assert.equal(await loadBogSdk('client', browser, doc), browser.BOG)
  assert.equal(attempt, 2)
})
