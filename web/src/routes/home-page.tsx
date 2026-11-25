import { Button } from "@/components/ui/button";

export function HomePage() {
  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-4">
      <h1 className="text-3xl font-semibold">PingWatch</h1>
      <Button>Get started</Button>
    </main>
  );
}
