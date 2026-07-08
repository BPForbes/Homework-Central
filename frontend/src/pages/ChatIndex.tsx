import { MessageSquare } from 'lucide-react'

/** Empty state when no channel is selected at /chat. */
export function ChatIndex() {
  return (
    <div className="flex-1 flex flex-col items-center justify-center bg-background text-center px-8">
      <div className="w-16 h-16 rounded-2xl bg-primary/10 flex items-center justify-center mb-4">
        <MessageSquare size={28} className="text-primary" />
      </div>
      <p className="text-lg font-semibold text-foreground">Select a channel</p>
      <p className="text-sm text-muted-foreground mt-1">
        Choose a channel from the sidebar to start chatting
      </p>
    </div>
  )
}
