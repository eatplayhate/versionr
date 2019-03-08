![Versionr Diagram](http://cygnus-core.net/versionr-diagram1.jpg)
The Versionr Revision Control System
===================

Welcome to the public repository for the Versionr SCM. Versionr is a new, largely untested (and probably deeply mediocre) source/data revision control system with similarities to existing systems such as [Git](http://git-scm.com), [Subversion](https://subversion.apache.org/) and [Mercurial](http://mercurial.selenic.com).

Versionr is currently in **alpha**, and is under active development. What this means for users will be covered later.

-----
# Contents
1. [Why does Versionr exist?](#why-does-versionr-exist)
1. [How functional is it?](#how-functional-is-it)
1. [Getting started](#getting-started)
1. [Cheatsheet](#cheatsheet)
1. [Contributing](#contributing)
1. [Credits & Attributions](#credits-attributions)
1. [Technical Info](#technical-info)

##Why does Versionr Exist?
>Versionr is like Tinder, but for versions!

###Features

- Distributed - all nodes (can be) equal
- No problems with large binary files, even with many stored versions
- Easy to branch, powerful merging system
- Network protocol is AES-256 encrypted and supports authentication
- Object and metadata storage is separated, allowing for shallow copies with full history
- Support for externs
- Open source, completely free
- Works on Windows, OSX and Linux
- Reasonably quick, resonably efficient
- Stated goal: to not be the worst option in all situations
- Exciting console experience

###Motivation
**TL;DR:** Versionr is a distributed, *git-esque*, SCM that is designed to handle large files as well as exciting branches and merges. It was built for the games industry in particular.

You may be thinking to yourself, *why do we need some weird Git knockoff?* In some regards, this question is quite valid - Git is a well made SCM that does exactly what it was designed to do very well. It allows deeply Linuxy people to get all their code and changes connected to the hivemind. I have no doubt that if I were a Linux kernel developer, I would be laughing into my fedoras at the idiocy of Versionr.

The *problem* with Git is, in many ways, a result of how good it is at what it does best. People look at Git and think "*hey, that SCM is a whole lot better than what we do, we should use it!*" In many cases, this is the right way to think; after all, there are people out there who think source control is a fancy way of saying "*get Timmy to zip up the folder at the end of the day*."

The issue for people in my industry in particular (game development) is that we want not only source in our SCM, but assets as well. Our repositories are typically only a few percent code, and littered with binary assets. Now this doesn't particularly fit the Git model of just funrolling your loops to get binary data, and while there are "fixes" that have been made (such as git-annex), fundamentally the Git SCM is not built to handle that kind of use case.

Other SCMs often do a better job at this - Perforce, for example, is great at binary assets; and even some of the more old-fashioned systems like Subversion handle them pretty well (Subversion is what we use in my studio, mainly because Perforce's entire design is an anathema to the way we work). Which brings us back to the problem - **Git is really good**, and the fact that it doesn't work for us is very frustrating.

Hence - Versionr. It's not Git; nor is it as good or mature as Git, but it works for a different use-case. It is designed specifically for large repositories with a high percentage of binary files. It is designed for a case where not all files need to be on all the nodes at once, allowing some number of *"servers"* which can keep all the data while typical users only check out shallow copies.

##How functional is it?

As I said before, Versionr is in **alpha**, in the sense that I have been working on it for about four weeks in my spare time. Now, while I will stand by what I've built, I'm not suggesting that you put all your eggs into the Versionr basket *right away*. For what it's worth, all my personal projects, including Versionr itself, are now using Versionr exclusively.

Even in it's current state, you **should not lose any data**. Just treat it with the level of discretion you'd expect from any open source project. The difference being that I am one person and haven't started calling myself *The Versionr Foundation* yet, so there's no sense of plausible deniability. Mind you, now that this is public, the ~~invisible hand of the free market~~ *many eyes* of the open source software community can help get it over the line.

You may have noticed that this is (at least originally) on GitHub. This isn't an admission of failure - it's just that I am under no illusions about the fact that there's no such thing as VersionrHub or Versionatorly or whatever the next cool name will be. For symmetry, I have put the full source of Git on my private Versionr server.

I have tested Versionr with quite old SVN repositories just to see how it copes under pressure. One of our test cases was a 60,000 revision project with about 60GB of checked-out data at the tip. It took a *long* time to get it into Versionr, but we found no problems with it.

##Getting Started

Versionr is written in the *one true language*, C#. If you're on Windows, A++, just download the code and build that sucker. I will put an actual binary download somewhere when I am more organized. If you're on a (I believe the polite term is) "*NIX", you have to at the very least start by installing [Mono](http://www.mono-project.com/).

Versionr has some native dependencies that will need to be compiled (although I guess I can make OSX builds - again, when I  am more organized). There is a *barely functional* makefile which, hopefully, will make a folder called "bin" that contains Versionr and all its friends once invoked.

>**Note**:
>For *reasons*, Versionr depends on external merging software to merge changes - although it will handle all the metadata tracking to decide the best merge strategy.
>
>On Windows, this will require that you install Kdiff3 or configure an external merge tool using the **.vrmeta** file. On Linux, **merge** must be available, or, alternatively, you can configure an external tool.

Regardless of your OS, once you jam it into your path, you can create a new empty Versionr *vault* using the **init** command, like this:

```
C:\Development> versionr init NewVault
Initializing new vault in location `D:\Development\NewVault\.versionr`.
Generating default .vrmeta file.
Version 34f52512-8e45-4db9-a230-6aa4d561a50a on branch "master" (rev 1)
```

For Linuxy people, I suggest making an alias which invokes Mono, so you don't end up wanting to kill yourselves when you use the program.

Now you have your vault, you can fill it with stuff and soon it will be time to immortalize it in the SCM. To do this, use the **record** command to select what you want to commit:
```
C:\Development\NewVault> versionr record *.cpp *.h
       (added)  lol.cpp
       (added)  wow.h

Recorded 2 objects.
```

And then use the **commit** command to jam it into the SCM:
```
C:\Development\NewVault> versionr commit -m "Putting all the things in"
<lots of words>
Updating internal state.
At version 6b7ccbe3-2f84-47e0-a3ad-39caa7f70517 on branch "master" (rev 2)
```
> **Note**:
> Advanced users can do both of these operations in a single step! Commit takes the same arguments as record for file selection, and will invoke record first if you specify any objects!

Now, let's assume you have a friend (or just another computer) that wants that data. Simply start the server process locally...
```
C:\Development\NewVault> versionr server
Server started, bound to [::]:5122.
```
And clone the repository on the remote computer:
```
[madhax@trilby ~]$ mono Versionr.exe clone --remote otherhostname vault
<loads of text>
0 updates, 2 additions, 0 deletions.
At version 6b7ccbe3-2f84-47e0-a3ad-39caa7f70517 on branch "master"
```

Now they can simply use the **push** and **pull** commands to synchronize data with your first node. Any number of nodes can be set up like this, and with luck, it will all work perfectly.

##Cheatsheet

#### <i class="icon-file"></i> Creating a Repository
**Making a new project**
```
// In the current folder
versionr init
// In another folder
versionr init destinationFolder
// With a specific branch name
versionr init --branch trunk
```
**Cloning a repository**
```
// From a server with only one hosted repository
versionr clone --remote servername
// Into a specific folder
versionr clone --remote servername destinationFolder
// From a specific port
versionr clone --remote servername:1111
// From a server with named repositories
versionr clone --remote vsr://servername/ProjectName
```
#### <i class="icon-pencil"></i> Viewing Prior Changes
**Show the log**
```
// Show the latest log entries
versionr log
// Show 50 entries in a concise form
versionr log -c -t50
// Show a log for a specific version which starts with "ab70"
versionr log -v ab70
```
#### <i class="icon-pencil"></i> Recording Changes
**Determining what has changed**
```
// Display a status of the file tree
versionr status
// Show the differences for a file
versionr diff changedfile.cpp
// Show all differences
versionr diff
```
**Including files in the next commit**
```
// Including all .cs files
versionr record *.cs
// Including all files in a subdirectory
versionr record subdirectoryName
// Including all files named "wow"
versionr record -n wow.*
// Including all changes
versionr record -a
```
**Marking a deleted object as being for reals deleted**
```
// Marking a specific (deleted) object as needing removal from the SCM
versionr record -d deletedFolderOrObject
// Including all changes, including deletions
versionr record -ad
```
**Actually putting data into Versionr**
```
// Creating a new version with the current changes applied
versionr commit -m "Message goes here"
```
#### <i class="icon-pencil"></i> Undoing Changes
**Removing an object from inclusion in the next commit**
```
// Unrecording a specific object
versionr unrecord wow.cpp
// Unrecording everything
versionr unrecord -a
// Unrecording all .h files
versionr unrecord *.h
```
**Undoing changes to a file**
```
// Reverting a specific object
versionr revert wow.cpp
// Reverting all files in a subdirectory
versionr revert subdirectoryName
```
**Nuking the whole repository**
```
// Check out a fresh copy of the whole repository
versionr checkout --force
// Check out a fresh copy and delete unversioned files
versionr checkout --force --purge
```
#### <i class="icon-file"></i> Branches
**Creating a new branch**
```
// Creating a new branch from the working copy
versionr branch branchname
```
**Checking out another branch**
```
// Checking out another branch in the repository
versionr checkout branchname
```
**Updating to the tip revision**
```
// Updating to the tip revision of the current branch
versionr update
```
**Merging changes**
```
// Merge changes from another branch
versionr merge branchname
// Merge changes, mark the other branch as pending deletion on your commit
versionr merge --reintegrate branchname
```
**Listing/deleting/renaming**
```
// List branches
versionr listbranch
// List deleted branches as well
versionr listbranch --deleted
// Rename the current branch
versionr renamebranch newname
// Rename a different branch
versionr renamebranch --branch oldname newname
// Delete the current branch
versionr deletebranch
// Delete a different branch
versionr deletebranch otherbranchname
```
#### <i class="icon-pencil"></i> Running a Server
```
// Run a versionr server on the default port
versionr server
// Run a versionr server on a special port
versionr server -p 1111
// Run a server with a server config file
versionr server --config Server.config
```

#### <i class="icon-pencil"></i> Communicating with a Server
**Configuring a server**
```
// Specifying a remote
versionr remote --remote hostname:1000 remotename
// Specifying a default remote
versionr remote --remote vsr://servername/ProjectName
```
**Getting data from the server**
```
// Get new versions for the current branch from the server and update your current workspace
versionr pull -u
// Get new versions for the current branch, but don't update the current checkout
versionr pull
// Get versions for a different branch
versionr pull -b otherbranch
// Get versions from a server that isn't the default (configured using the remote command)
versionr pull -u remotename
// Get versions from a server that isn't the default and isn't a named remote
versionr pull -u --remote otherhostname:7777
```
**Putting data into the server**
```
// Push your current branch changes
versionr push remotename
// Push a different branch
versionr push -b otherbranch
// Push to a remote that isn't a named remote
versionr push --remote otherhostname:7777
```


##Contributing
Please do, for the love of god. I can't do this all myself. I am not a precious person, the code is terrible, just help it get better.

##Credits & Attribution
Versionr is built on things made by other people - specifically:

- [SQLite](https://www.sqlite.org/), which thankfully does all the hard work
- [SQLite.Net](https://github.com/praeclarum/sqlite-net), an ORM for C#+sqlite
- [LZHAM](https://github.com/richgel999/lzham_codec), a compression codec that seems to have been gifted to us from the future
- [LZHL](https://github.com/ryandrake08/lzhl), which Versionr uses for network compression
- [protobuf.net](https://github.com/mgravell/protobuf-net), a C# protocol buffer system
- [commandline](https://github.com/gsscoder/commandline), a C# command-line parser
- [Octodiff](https://github.com/OctopusDeploy/Octodiff) which Versionr doesn't use, but is the basis of the delta-compression algorithm

##Technical Information

TBD