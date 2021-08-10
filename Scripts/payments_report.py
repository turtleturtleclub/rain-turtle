import json
from sys import argv

BOT_ADDRESS = 'TRTLv1m7FFnTybogRLyaaJDmmGVwae98j68L28AsZ6VxBLgHdjotEhu6HoYd4BpAiuSXLVqxbXEybHykFoH5Vr1h3HacVLo73p1'

amount_transaction = 0
amount_incoming = 0
amount_outgoing = 0
amount_sent_to_others = 0
amount_sent_to_self = 0
amount_change = 0
amount_fees = 0

if len(argv) != 2:
    print('File path of payments file is needed as first argument.')
    exit()

with open(argv[1], 'r') as payments_file:
    payments_json = json.load(payments_file)

    for item in payments_json['items']:
        for transaction in item['transactions']:
            amount_transaction += transaction['amount']

            if transaction['amount'] < 0:
                amount_fees += transaction['fee']

            for transfer in transaction['transfers']:
                address = transfer['address']
                amount = transfer['amount']
                type = transfer['type']
                if address == BOT_ADDRESS:
                    if type == 2:
                        amount_change += amount
                    else:
                        if amount > 0:
                            if transaction['amount'] > 0:
                                amount_incoming += amount
                            else:
                                amount_sent_to_self += amount
                        else:
                            amount_outgoing -= amount
                elif address != '':
                    amount_sent_to_others += amount

print('=======================================================================')
print('Report of @FranklinRain (Incoming / Outgoing Balance):')
print('=======================================================================')
print('{:40} {:20,.2f} TRTL'.format('Total Amount Incoming', amount_incoming / 100))
print('    {:36} {:20,.2f} TRTL'.format('- Total Amount Outgoing (Raw)', amount_outgoing / 100))
print('    {:36} {:20,.2f} TRTL'.format('+ Change Transfers', amount_change / 100))
print('    {:36} {:20,.2f} TRTL'.format('+ Transfers To Self', amount_sent_to_self / 100))
print('-----------------------------------------------------------------------')
print('{:40} {:20,.2f} TRTL'.format('Balance', (amount_incoming - amount_outgoing + amount_change + amount_sent_to_self) / 100))
print('    {:36} {:20,.2f} TRTL'.format('- Balance reported by RPC', amount_transaction / 100))
print('-----------------------------------------------------------------------')
print('{:40} {:20,.2f} TRTL'.format('Difference (should be 0)', (amount_incoming - amount_outgoing + amount_change + amount_sent_to_self - amount_transaction) / 100))
print('=======================================================================')
print()
print('=======================================================================')
print('Report of @FranklinRain (Amount sent to others):')
print('=======================================================================')
print('{:40} {:20,.2f} TRTL'.format('Total Amount Incoming', amount_incoming / 100))
print('    {:36} {:20,.2f} TRTL'.format('- Amount sent to others', amount_sent_to_others / 100))
print('    {:36} {:20,.2f} TRTL'.format('- Fees', amount_fees / 100))
print('-----------------------------------------------------------------------')
print('{:40} {:20,.2f} TRTL'.format('Balance', (amount_incoming - amount_sent_to_others - amount_fees) / 100))
print('    {:36} {:20,.2f} TRTL'.format('- Balance reported by RPC', amount_transaction / 100))
print('-----------------------------------------------------------------------')
print('{:40} {:20,.2f} TRTL'.format('Difference (should be 0)', (amount_incoming - amount_sent_to_others - amount_fees - amount_transaction) / 100))
print('=======================================================================')
