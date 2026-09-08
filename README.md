# How to Put Your Code on GitHub

This repository contains a guide on how to upload and maintain code on GitHub using Git.

## Prerequisites
1. **Install Git**: Make sure Git is installed on your computer.
2. **GitHub Account**: Ensure you have a GitHub account created at [github.com](https://github.com).

---

## Step-by-Step Guide

### 1. Initialize Git in Your Project Folder
If your local project is not already a Git repository, open your terminal/command prompt in your project directory and run:
```bash
git init
```

### 2. Add Your Files to Staging
Add all your project files to the Git staging area:
```bash
git add .
```

### 3. Commit Your Changes
Save your changes locally with a descriptive commit message:
```bash
git commit -m "Initial commit"
```

### 4. Create a New Repository on GitHub
1. Go to [GitHub New Repository](https://github.com/new).
2. Enter a repository name and choose whether it should be **Public** or **Private**.
3. Do **not** check "Initialize this repository with a README" if you already created files locally.
4. Click **Create repository**.

### 5. Link Your Local Repository to GitHub
Copy the remote repository URL from GitHub and add it to your local git setup:
```bash
git remote add origin <REMOTE_REPOSITORY_URL>
```
*(Example: `git remote add origin https://github.com/username/repository-name.git`)*

### 6. Rename Default Branch (Optional but Recommended)
Set your default branch name to `main`:
```bash
git branch -M main
```

### 7. Push Your Code to GitHub
Push your local code to GitHub:
```bash
git push -u origin main
```

---

## Updating Code on GitHub Later
Whenever you make updates to your project:
1. `git add .`
2. `git commit -m "Describe your changes"`
3. `git push`
