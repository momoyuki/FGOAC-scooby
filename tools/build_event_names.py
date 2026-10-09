"""Writes src\\FGOLocalPlatform.EventNames.json, the English titles of the events in Cloud23333's event catalogue.

  python tools\\build_event_names.py <English import>.json <his Server>\\artemis\\titles\\fgo\\activity_display_catalog.json src\\FGOLocalPlatform.EventNames.json

Each catalogue event is named by the Japanese title of a coop_event_setting row; its English title is that row's
translation in the English import (rows of {"id", "原文", "译文"}). An event without one is left out, and the Events page
shows its Japanese title.
"""
import json
import sys

importPath, catalogPath, outPath = sys.argv[1:4]
rows = json.load(open(importPath, encoding='utf-8'))
catalog = json.load(open(catalogPath, encoding='utf-8-sig'))
english = {}
for row in rows:
    if row['id'].startswith('rom/mst_data/arms_mst_coop_event_setting'):
        english.setdefault(row['原文'], row['译文'])
names = {}
for event in catalog['events']:
    if event['name'] in english:
        names[str(event['id'])] = english[event['name']]
with open(outPath, 'w', encoding='utf-8', newline='\n') as out:
    json.dump(dict(sorted(names.items(), key=lambda pair: int(pair[0]))), out, ensure_ascii=False, indent=2)
    out.write('\n')
print(len(names), 'of', len(catalog['events']))
