"""Compare direct HTTP/WS actions with batch polling in a paused Story session."""
import argparse
import asyncio
import json
from pathlib import Path
import time
from profile_live import distribution, Process
from gta_use import Client


async def run(args):
    game = Client(process=args.process)
    process = Process(game.service['serviceProcess'])
    report = {'gameProcess': args.process, 'serviceProcess': game.service['serviceProcess'],
              'backend': game.service['backend'], 'session': game.session, 'phases': []}
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    def save():
        output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    def record(name, latencies, frames, before, seconds):
        after = process.read()
        report['phases'].append({'name': name, 'latencyMs': distribution(latencies),
                                'frameStride': distribution([b-a for a,b in zip(frames, frames[1:])]),
                                'cpuMsPerCall': (after['cpuSeconds']-before['cpuSeconds'])*1000/len(latencies),
                                'before': before, 'after': after, 'elapsedSeconds': seconds})
        save()
        print(json.dumps(report['phases'][-1]), flush=True)
    command = dict(op='native', name='GET_FRAME_COUNT', args=[], returns='int')
    try:
        game.command('Thor.Thor.Current.SetEnabled(false);')
        game.action('pause')
        for name, call, is_frame in [
            ('http-state-player', lambda: game.state(sections=['player']), False),
            ('http-native-direct', lambda: game.native('GET_FRAME_COUNT', returns='int'), True),
            ('http-native-via-batch', lambda: game.batch([command])[0], True)]:
            for _ in range(5): call()
            before = process.read(); started = time.perf_counter(); latencies=[]; frames=[]
            for _ in range(args.samples):
                tick = time.perf_counter(); result = call()
                latencies.append((time.perf_counter()-tick)*1000)
                if is_frame: frames.append(result)
            record(name, latencies, frames, before, time.perf_counter()-started)
        async with game.connect() as live:
            for _ in range(5): await live.native('GET_FRAME_COUNT', returns='int')
            before = process.read(); started=time.perf_counter(); latencies=[]; frames=[]
            for _ in range(args.samples):
                tick=time.perf_counter(); frames.append(await live.native('GET_FRAME_COUNT', returns='int'))
                latencies.append((time.perf_counter()-tick)*1000)
            record('ws-native-direct', latencies, frames, before, time.perf_counter()-started)
        report['status'] = game.request('status')
        report['requestFiles'] = len(list(game.directory.glob('request-*.json')))
        report['responseFiles'] = len(list(game.directory.glob('response-*.json')))
        assert report['requestFiles'] == report['responseFiles'] == 0
        assert report['status']['paused'] and report['status']['droppedRecords'] == 0
    finally:
        save(); game.close(); process.close()


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--process', type=int, required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--samples', type=int, default=200)
    args=parser.parse_args()
    if not 10 <= args.samples <= 10000: parser.error('Use 10..10000 samples.')
    asyncio.run(run(args))
