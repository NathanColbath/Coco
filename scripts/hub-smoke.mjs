// Smoke test for the realtime generation channel.
//
//   node scripts/hub-smoke.mjs <jwt> <storyId>
//
// Connects to /hubs/generation as the signed-in user, triggers a generation and
// prints the streamed events. Without a NovelAI token the run still exercises
// auth, group membership and the failure path end to end.

import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

const [token, storyId] = process.argv.slice(2);
const base = process.env.API_BASE ?? 'http://localhost:5240';

if (!token || !storyId) {
  console.error('usage: node scripts/hub-smoke.mjs <jwt> <storyId>');
  process.exit(2);
}

const connection = new HubConnectionBuilder()
  .withUrl(`${base}/hubs/generation`, { accessTokenFactory: () => token })
  .configureLogging(LogLevel.Warning)
  .build();

const done = (code) => {
  connection.stop().finally(() => process.exit(code));
};

connection.on('generation.started', (event) =>
  console.log(`\n[${event.generationId}] started (${event.model})`),
);
connection.on('generation.token', (event) => process.stdout.write(event.text));
connection.on('generation.completed', (event) => {
  console.log(`\ncompleted in ${event.durationMs}ms, block ${event.block.id}`);
  done(0);
});
connection.on('generation.failed', (event) => {
  console.log(`\nfailed: ${event.message}`);
  done(0);
});

await connection.start();
console.log(`connected to ${base}/hubs/generation`);

const response = await fetch(`${base}/api/stories/${storyId}/generate`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
  body: JSON.stringify({}),
});
console.log(`generate -> ${response.status} ${await response.text()}`);

setTimeout(() => {
  console.log('\ntimed out waiting for an event');
  done(1);
}, 30_000);
