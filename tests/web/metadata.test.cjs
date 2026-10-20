const test = require('node:test');
const assert = require('node:assert/strict');
const panel = require('../../Jellyfin.Plugin.MetaTube/Web/metadata.js');

test('only item details routes trigger metadata requests', () => {
  const id = '0123456789abcdef0123456789abcdef';
  assert.equal(panel.itemIdFromLocation({hash:'#!/details?id='+id}), id);
  assert.equal(panel.itemIdFromLocation({hash:'#/details?id='+id}), id);
  assert.equal(panel.itemIdFromLocation({hash:'#/home?id='+id}), null);
  assert.equal(panel.itemIdFromLocation({hash:'#/details?id=../../token'}), null);
  assert.equal(panel.itemIdFromLocation({hash:'#/details?parentId='+id}), null);
});
test('detail routes must belong to the active Jellyfin server', () => {
  assert.equal(panel.matchesServer({hash:'#!/details?serverId=one'}, 'one'), true);
  assert.equal(panel.matchesServer({hash:'#!/details?serverId=two'}, 'one'), false);
  assert.equal(panel.matchesServer({hash:'#!/details?id=abc'}, 'one'), true);
  assert.equal(panel.matchesServer({hash:'#!/details?serverId=two'}, null), false);
});
test('ratings retain source scale and reject malformed values', () => {
  assert.equal(panel.formatRating({value:4.2,scale_max:5}), '4.2 / 5');
  assert.equal(panel.formatRating({value:8.4,scale_max:10}), '8.4 / 10');
  assert.equal(panel.formatRating({value:12,scale_max:5}), null);
  assert.equal(panel.formatRating({value:NaN,scale_max:5}), null);
});
test('append pages without duplicate source review IDs', () => {
  const first = [{id:'a',source:'A',comment:'One'}];
  const next = [{id:'a',source:'A',comment:'One'},{id:'a',source:'B',comment:'Two'}];
  assert.equal(panel.mergeReviews(first,next).length,2);
  assert.equal(panel.mergeReviews([], [{author:'A',comment:'One'},{author:'B',comment:'Two'}]).length,2);
});
