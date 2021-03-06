import { Observable, ReplaySubject } from 'rxjs';

import { IPipeClientStream } from '../../foundation/pipes';
import { CancellationTokenSource, CancellationToken } from '../../foundation/threading';
import { IAsyncDisposable } from '../../foundation/disposable';
import { ArgumentNullError } from '../../foundation/errors';

import * as WireMessage from './wire-message';
import { SerializationPal } from './serialization-pal';
import { MessageEvent } from './message-event';
import { Trace } from '../../foundation/utils/trace';

/* @internal */
export interface IMessageStream extends IAsyncDisposable {
    readonly isConnected: boolean;
    readonly messages: Observable<MessageEvent>;

    writeAsync(wireMessage: WireMessage.Request | WireMessage.Response, cancellationToken: CancellationToken): Promise<void>;
}

/* @internal */
export class MessageStream implements IMessageStream {
    private static readonly _trace = Trace.category('MessageStream');

    private readonly _messages = new ReplaySubject<MessageEvent>();
    private readonly _readIndefinitelyCts = new CancellationTokenSource();
    private readonly _readIndefinitelyPromise: Promise<void>;
    private readonly _headerBuffer = Buffer.alloc(5);

    private _isConnected = true;

    public get messages(): Observable<MessageEvent> { return this._messages; }
    public get isConnected(): boolean { return this._isConnected; }

    constructor(private readonly _stream: IPipeClientStream) {
        if (!_stream) { throw new ArgumentNullError('stream'); }

        MessageStream._trace.log(`instance created`);

        this._readIndefinitelyPromise = this.readIndefinitelyAsync(this._readIndefinitelyCts.token);
    }

    public writeAsync(wireMessage: WireMessage.Request | WireMessage.Response, cancellationToken: CancellationToken): Promise<void> {
        return this._stream.writeAsync(
            SerializationPal.wireMessageToBuffer(wireMessage),
            cancellationToken
        );
    }

    public async disposeAsync(): Promise<void> {
        MessageStream._trace.log(`method:disposeAsync() is running`);

        this._messages.complete();
        await this._stream.disposeAsync();
        this._readIndefinitelyCts.cancel(false);

        try {
            await this._readIndefinitelyPromise;
        } catch (error) {
        }
    }

    private async readIndefinitelyAsync(token: CancellationToken): Promise<void> {
        try {
            while (!token.isCancellationRequested) {
                MessageStream._trace.log(`method "readIndefinitelyAsync": running cycle`);
                const message = await this.readMessageAsync(token);

                this._messages.next(new MessageEvent(this, message));
            }
        } catch (error) {
            this._isConnected = false;
        }
    }

    private async readMessageAsync(token: CancellationToken): Promise<WireMessage.Request | WireMessage.Response> {
        await this._stream.readAsync(this._headerBuffer, token);
        const type = this._headerBuffer.readUInt8(0) as WireMessage.Type;
        const length = this._headerBuffer.readInt32LE(1);

        const bytes = Buffer.alloc(length);
        await this._stream.readAsync(bytes, token);

        const json = bytes.toString('utf8');
        const message = SerializationPal.fromJson(json, type);
        return message;
    }
}
