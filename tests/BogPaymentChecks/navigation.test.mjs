import test from 'node:test'
import assert from 'node:assert/strict'
import { pageUrl, pageFromUrl } from '../../../agrotrade-front/src/services/navigation.js'

test('products have stable database IDs and readable names', () => {
  const url = pageUrl({ page: 'product', productId: 'db-123', productName: 'ბუფალო 170F' })
  assert.ok(url.startsWith('/products/123/'))
  assert.match(decodeURIComponent(url), /ბუფალო-170f/)
  assert.deepEqual(pageFromUrl(url), { page: 'product', productId: 'db-123' })
  assert.deepEqual(pageFromUrl('/products/123/old-name'), { page: 'product', productId: 'db-123' })
})
test('category and search URLs round trip without losing Georgian characters', () => {
  const state = { page: 'products', category: 'მოტობლოკები / ნაწილები', search: 'ბუფალო & 170', discounted: true }
  assert.deepEqual(pageFromUrl(pageUrl(state)), state)
})
test('checkout steps, cart and sections open directly', () => {
  for (const state of [{ page: 'cart' }, { page: 'checkout', step: 2 }, { page: 'checkout', step: 3 }, { page: 'home', section: 'service-info' }, { page: 'home', section: 'contact' }]) {
    assert.deepEqual(pageFromUrl(pageUrl(state)), state)
  }
  assert.deepEqual(pageFromUrl('/checkout/complete'), { page: 'checkout', step: 3 })
  assert.deepEqual(pageFromUrl('/'), { page: 'home' })
})
test('payment callbacks survive canonical URL and refresh without carrying secret parameters', () => {
  const state = pageFromUrl('/?payment=success&provider=liberty&order=AT-1&token=secret')
  const url = pageUrl(state)
  assert.ok(url.startsWith('/payment/result?'))
  assert.equal(url.includes('secret'), false)
  assert.deepEqual(pageFromUrl(url), state)
  assert.equal(new URLSearchParams(state.query).get('provider'), 'liberty')
})
test('subdirectory hosting and malformed links are handled', () => {
  assert.deepEqual(pageFromUrl(pageUrl({ page: 'product', productId: 'db-4' }, '/shop/'), '/shop/'), { page: 'product', productId: 'db-4' })
  for (const path of ['/products/not-a-number', '/not-existing', '/categories/%ZZ']) assert.equal(pageFromUrl(path).page, 'not-found')
  assert.deepEqual(pageFromUrl('/#contact'), { page: 'home', section: 'contact' })
})
