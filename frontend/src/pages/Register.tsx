import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import { useCaptcha } from '../hooks/useCaptcha'
import { Captcha } from '../components/Captcha'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Label } from '../components/ui/label'

export function Register() {
  const { register } = useAuth()
  const navigate = useNavigate()
  const captcha = useCaptcha()
  const [email, setEmail] = useState('')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError('')

    if (!email.trim()) {
      setError('Email is required.')
      return
    }
    const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
    if (!emailPattern.test(email)) {
      setError('Please enter a valid email address.')
      return
    }
    if (!username.trim()) {
      setError('Username is required.')
      return
    }
    if (username.trim().length < 3 || username.trim().length > 64) {
      setError('Username must be between 3 and 64 characters.')
      return
    }
    if (password.length < 8) {
      setError('Password must be at least 8 characters.')
      return
    }
    if (password.length > 128) {
      setError('Password must be 128 characters or fewer.')
      return
    }
    if (password !== confirm) {
      setError('Passwords do not match.')
      return
    }

    const captchaStarted =
      captcha.assessing ||
      captcha.phase === 'fcaptcha-only-ready' ||
      captcha.phase === 'puzzle' ||
      Boolean(captcha.fCaptchaToken)

    if (captchaStarted && !captcha.canSubmit) {
      setError('Complete the verification check before creating your account.')
      return
    }

    const submission = captcha.buildSubmission()

    setIsSubmitting(true)
    try {
      await register(email.trim().toLowerCase(), username.trim(), password, submission ?? undefined)
      navigate('/dashboard')
    } catch (err: unknown) {
      void captcha.refresh()
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        'Registration failed. Please try again.'
      setError(msg)
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div className="bg-card rounded-lg shadow-sm border border-border p-10 w-full max-w-[420px]">
        <p className="text-sm font-medium text-primary mb-1">Homework Central</p>
        <h1 className="text-2xl font-semibold text-foreground mb-7">Create account</h1>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="email">Email</Label>
            <Input
              id="email"
              type="email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              disabled={isSubmitting}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="username">Username</Label>
            <Input
              id="username"
              type="text"
              autoComplete="username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              required
              minLength={3}
              maxLength={64}
              disabled={isSubmitting}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="password">Password</Label>
            <Input
              id="password"
              type="password"
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              minLength={8}
              disabled={isSubmitting}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="confirm">Confirm password</Label>
            <Input
              id="confirm"
              type="password"
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              required
              disabled={isSubmitting}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="captcha-answer">
              Verify you&apos;re human (optional — get Verified status immediately)
            </Label>
            <Captcha captcha={captcha} inputId="captcha-answer" disabled={isSubmitting} />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <Button type="submit" className="w-full" disabled={isSubmitting || captcha.assessing}>
            {isSubmitting ? 'Creating account…' : 'Create account'}
          </Button>
        </form>
        <p className="text-center text-sm text-muted-foreground mt-5">
          Already have an account?{' '}
          <Link to="/login" className="text-primary font-medium hover:underline">
            Sign in
          </Link>
        </p>
      </div>
    </div>
  )
}
