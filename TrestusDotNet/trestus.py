from argparse import ArgumentParser
from datetime import datetime
from os import environ, path
from shutil import copy2
from sys import exit
from yaml import load as load_yaml

from jinja2 import Environment, FileSystemLoader
from trello import TrelloClient
from mistune import Markdown


def main():
    parser = ArgumentParser(description='Generate a status page from a Trello '
                                        'board')
    parser.add_argument('-k', '--key', dest='key',
                        default=environ.get('TRELLO_KEY'),
                        help='Trello API key')
    parser.add_argument('-s', '--secret', dest='secret',
                        default=environ.get('TRELLO_SECRET'),
                        help='Trello API secret')
    parser.add_argument('-t', '--token', dest='token',
                        default=environ.get('TRELLO_TOKEN'),
                        help='Trello API auth token')
    parser.add_argument('-S', '--token-secret', dest='token_secret',
                        default=environ.get('TRELLO_TOKEN'),
                        help='Trello API auth token secret')
    parser.add_argument('-b', '--board-id', dest='board',
                        default=environ.get('TRELLO_BOARD_ID'),
                        help='Trello board ID')
    parser.add_argument('-T', '--custom-template', dest='template',
                        help='Custom jinja2 template to use instead of default')
    parser.add_argument('-d', '--template-data', dest='template_data',
                        help='If using --custom-template, you can provide a '
                             'YAML file to load in data that would be '
                             'available in the template the template')
    parser.add_argument('--skip-css', dest='skip_css', action='store_true',
                        help='Skip copying the default trestus.css to the '
                             'output dir.')
    parser.add_argument('output_path', help='Path to output rendered HTML to')
    args = parser.parse_args()

    client = TrelloClient(
        api_key=args.key,
        api_secret=args.secret,
        token=args.token,
        token_secret=args.token_secret)

    markdown = Markdown()
    board = client.get_board(args.board)
    labels = board.get_labels()
    service_labels = [l for l in labels if not l.name.startswith('status:')]
    service_ids = [s.id for s in service_labels]
    status_types = [l for l in labels if l not in service_labels]
    lists = board.open_lists()

    incidents = []
    panels = {}
    systems = {}

    for card_list in lists:
        cards = card_list.list_cards()
        cards.sort(key=lambda c: c.create_date, reverse=True)
        for card in cards:
            severity = None
            for label in card.labels:
                if not label.name.startswith('status:'):
                    continue

                severity = label.name.lstrip('status:').lstrip()
                if label.color == 'red':
                    break
            card.severity = severity


            card_service_labels = [l.name for l in card.labels if l.id in service_ids]
            if not card_service_labels or not card.severity:
                continue

            if card_list.name.lower() == 'fixed':
                card.closed = True
            else:
                if card.severity not in panels:
                    panels[card.severity] = []
                panels[card.severity] += card_service_labels

                for service in card_service_labels:
                    if service not in systems:
                        systems[service] = {'status': card_list.name,
                                            'severity': card.severity}

            card.html_desc = markdown(card.desc)

            # Working around a bug in latest py-trello which fails to
            # load/parse comments properly
            comments = card.fetch_comments(force=True)
            for comment in comments:
                comment['html_desc'] = markdown(comment['data']['text'])
                comment['parsed_date'] = datetime.strptime(
                    comment['date'].replace('-', '').replace(':', ''),
                    '%Y%m%dT%H%M%S.%fZ')

            card.parsed_comments = comments

            incidents.append(card)

    for label in service_labels:
        if label.name not in systems:
            systems[label.name] = {'status': 'Operational', 'severity': ''}

    if args.template_data:
        template_data = load_yaml(open(args.template_data))
    else:
        template_data = {}


    env = Environment(loader=FileSystemLoader(
        path.join(path.dirname(__file__), 'templates')))
    if args.template:
        with open(args.template) as f:
            template = env.from_string(f.read())
    else:
        template = env.get_template('trestus.html')

    with open(args.output_path, 'w+') as f:
        f.write(template.render(incidents=incidents, panels=panels,
                                systems=systems, **template_data))

    if not args.skip_css:
        css_path = path.join(path.dirname(__file__), 'templates',
                             'trestus.css')
        copy2(css_path, path.dirname(args.output_path))

    print('Status page written to {}'.format(args.output_path))
    return 0


if __name__ == '__main__':
    exit(main())