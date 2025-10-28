import { Chat, Message, MessageFragment } from '../types/ChatTypes';
import * as signalR from '@microsoft/signalr';
import { UnboundedChannel } from '../utils/UnboundedChannel';

class ChatService {
    private static instance: ChatService;
    private hubConnection?: signalR.HubConnection;
    private initialized = false;
    private backendUrl: string;
    private activeStreams = new Map<string, UnboundedChannel<MessageFragment>>();

    private constructor(backendUrl: string) {
        this.backendUrl = backendUrl;
    }

    static getInstance(backendUrl: string): ChatService {
        if (!ChatService.instance) {
            ChatService.instance = new ChatService(backendUrl);
        }
        return ChatService.instance;
    }

    async ensureInitialized(): Promise<void> {
        if (!this.hubConnection && !this.initialized) {
            console.debug('Initializing SignalR connection...');
            this.hubConnection = new signalR.HubConnectionBuilder()
                .withUrl(`${this.backendUrl}/stream`, {
                    skipNegotiation: true,
                    transport: signalR.HttpTransportType.WebSockets
                })
                .withStatefulReconnect()
                .withAutomaticReconnect({
                    nextRetryDelayInMilliseconds: retryContext => {
                        // We want to retry forever
                        if (retryContext.elapsedMilliseconds > 15 * 1000) {
                            // Max 15 seconds delay
                            return 15000;
                        }

                        // Otherwise, we want to retry every second
                        return (retryContext.previousRetryCount + 1) * 1000;
                    }
                })
                .build();

            this.hubConnection.onreconnected(async () => {
                console.debug('Reconnected to SignalR hub');

                for (const channel of this.activeStreams.values()) {
                    channel?.close();
                }
                this.activeStreams.clear();
            });

            await this.hubConnection.start();
            this.initialized = true;
        }
    }

    async getChats(): Promise<Chat[]> {
        const response = await fetch(`${this.backendUrl}`);
        if (!response.ok) {
            throw new Error('Error fetching chats');
        }
        return await response.json();
    }

    async getChatMessages(chatId: string): Promise<Message[]> {
        const response = await fetch(`${this.backendUrl}/${chatId}`);
        if (!response.ok) {
            throw new Error('Error fetching chat messages');
        }
        return await response.json();
    }

    async createChat(name: string): Promise<Chat> {
        const response = await fetch(`${this.backendUrl}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ name })
        });
        if (!response.ok) {
            throw new Error('Failed to create chat');
        }
        return await response.json();
    }

    async *stream(
        id: string,
        initialLastMessageId: string | null,
        abortController: AbortController
    ): AsyncGenerator<MessageFragment> {
        await this.ensureInitialized();

        if (!this.hubConnection) {
            throw new Error('ChatService not initialized');
        }

        let lastFragmentId: string | undefined;
        let lastMessageId = initialLastMessageId;

        // Set up and store the abort event handler
        const abortHandler = () => {
            console.log(`Aborting stream for chat: ${id}`);
            this.activeStreams.get(id)?.close();
        };

        abortController.signal.addEventListener('abort', abortHandler);

        try {
            while (!abortController.signal.aborted) {
                let channel = new UnboundedChannel<MessageFragment>();
                this.activeStreams.set(id, channel);

                let subscription = this.hubConnection.stream("Stream", id, { lastMessageId, lastFragmentId })
                    .subscribe({
                        next: (value) => {
                            const fragment: MessageFragment = {
                                id: value.id,
                                sender: value.sender,
                                text: value.text,
                                isFinal: value.isFinal ?? false,
                                fragmentId: value.fragmentId
                            };
                            lastFragmentId = fragment.fragmentId;
                            if (fragment.isFinal) {
                                lastMessageId = fragment.id;
                            }
                            channel.write(fragment);
                        },
                        complete: () => {
                            console.debug(`Stream completed for chat: ${id}`);
                            channel.close();
                        },
                        error: (err) => {
                            // Don't close the channel on error, if the connection breaks, signalr will reconnect
                            // and we'll close the channel
                        }
                    });

                try {
                    for await (const fragment of channel) {
                        yield fragment;
                    }
                } catch (error) {
                    console.error('Stream error:', error);
                    // Only break the loop if we're aborting
                    if (abortController.signal.aborted) {
                        break;
                    }
                } finally {
                    subscription?.dispose();
                    this.activeStreams.delete(id);
                }

                // If we're not aborting, wait a second before retrying
                if (!abortController.signal.aborted) {
                    await new Promise(resolve => setTimeout(resolve, 1000));
                }
            }
        } finally {
            abortController.signal.removeEventListener('abort', abortHandler);
        }
    }

    async sendPrompt(id: string, prompt: string): Promise<void> {
        const response = await fetch(`${this.backendUrl}/${id}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text: prompt })
        });

        if (!response.ok) {
            let errorMessage;
            try {
                errorMessage = await response.text();
            } catch (e) {
                errorMessage = response.statusText;
            }
            throw new Error(`Error sending prompt: ${errorMessage}`);
        }
    }

    async deleteChat(id: string): Promise<void> {
        const response = await fetch(`${this.backendUrl}/${id}`, {
            method: 'DELETE'
        });

        if (!response.ok) {
            throw new Error('Failed to delete chat');
        }
    }

    async cancelChat(id: string): Promise<void> {
        const response = await fetch(`${this.backendUrl}/${id}/cancel`, {
            method: 'POST'
        });
        if (!response.ok) {
            throw new Error('Failed to cancel chat');
        }
    }
}

export default ChatService;
